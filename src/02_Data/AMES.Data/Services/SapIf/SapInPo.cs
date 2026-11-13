using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Services.SapIf;

/// <summary>
/// IF-T04 구매 PO 수신: EOS_IF.IF_TX_PO_HDR 1행 + IF_TX_PO_LINE N행(B1 PO 전 라인 스냅샷) → MES 구매발주 <c>WH_PurchaseOrder</c>(라인 단위 행).
/// 키 = (B1DocEntry, B1LineNum) — <c>dist/migrate_sap_if_po_mrp.sql</c> 이 만든 컬럼. PoNumber 에는 B1 문서번호(DocNum)를 문자열로 넣는다.
/// 정책: SAP 우선 — 잔량·수량·납기·상태를 B1 값으로 덮어쓴다(정의서: MES 와 B1 잔량이 다르면 B1 기준).
/// 취소(X)/종료(C) 는 삭제가 아니라 상태(Cancelled/Complete)로 둔다. 상태 어휘는 기존 MES 값(Open·Partial·Complete·Received·Cancelled).
/// </summary>
internal static class SapInPo
{
    public const string LineEosTable = "IF_TX_PO_LINE";

    private static readonly string[] Cols =
        ["PoNumber", "PoLineNo", "VendorID", "ItemNo", "OrderQty", "ReceivedQty", "UnitCode", "UnitPrice",
         "Currency", "OrderDate", "DueDate", "Status"];

    public static (int Affected, char Kind, string Note) Apply(SqlConnection conn, SqlTransaction tx,
        IReadOnlyDictionary<string, object?> h, IReadOnlyList<Dictionary<string, object?>> lines)
    {
        var docEntry  = h["B1_DOCENTRY"];
        var docStatus = h.GetValueOrDefault("DocStatus") as string;
        int affected = 0, created = 0, changed = 0, same = 0;
        var changes = new List<string>();

        foreach (var l in lines)
        {
            var incoming = Incoming(h, l);
            var existing = ReadExisting(conn, tx, docEntry, l["LINENUM"]);
            char kind;
            if (existing is null) { kind = 'N'; created++; }
            else
            {
                var diffs = SapIfRules.Diff(existing, incoming, Cols, []);
                if (diffs.Count == 0) { kind = 'S'; same++; }
                else
                {
                    kind = 'C'; changed++;
                    if (changes.Count < 5) changes.Add($"[L{l["LINENUM"]} {SapIfRules.FormatDiff(diffs)}]");
                }
            }
            affected += Merge(conn, tx, docEntry, l["LINENUM"], incoming);
            _ = kind;
        }

        // 헤더 취소(X): 스냅샷에 라인이 없어도 그 PO 의 MES 행을 모두 취소 처리한다.
        int cancelled = 0;
        if (SapIfRules.PoStatus(docStatus, null, 1m, 1m) == "Cancelled")
            cancelled = SetStatus(conn, tx, docEntry, "Cancelled");
        affected += cancelled;

        var kindAll = created > 0 && changed == 0 && same == 0 ? 'N' : (changed > 0 || cancelled > 0 || created > 0 ? 'C' : 'S');
        var note = $"PO DocEntry {docEntry} 상태 {docStatus}; 라인 {lines.Count}건: 신규 {created}·변경 {changed}·동일 {same}"
                 + (cancelled > 0 ? $"·취소 처리 {cancelled}" : "")
                 + (changes.Count > 0 ? " " + string.Join(" ", changes) + (changed > changes.Count ? " …" : "") : "");
        return (affected, kindAll, note);
    }

    /// <summary>B1 헤더·라인 → WH_PurchaseOrder 값.</summary>
    internal static Dictionary<string, object?> Incoming(IReadOnlyDictionary<string, object?> h, IReadOnlyDictionary<string, object?> l)
    {
        decimal order = Dec(l.GetValueOrDefault("OrderQty"));
        decimal open  = Dec(l.GetValueOrDefault("OpenQty"));
        var status = SapIfRules.PoStatus(h.GetValueOrDefault("DocStatus") as string, l.GetValueOrDefault("LineStatus") as string, order, open);
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["PoNumber"]    = h["B1_DOCNUM"]?.ToString(),
            ["PoLineNo"]    = l["LINENUM"],
            ["VendorID"]    = h.GetValueOrDefault("VendorID"),
            ["ItemNo"]      = l.GetValueOrDefault("ItemNo"),
            ["OrderQty"]    = order,
            ["ReceivedQty"] = Math.Max(0m, order - open),            // 입고 누계 = 발주 − 미입고 잔량(B1 기준)
            ["UnitCode"]    = l.GetValueOrDefault("UOM"),
            ["UnitPrice"]   = l.GetValueOrDefault("Price"),
            ["Currency"]    = h.GetValueOrDefault("CurrencyCode"),
            ["OrderDate"]   = h.GetValueOrDefault("DocDate"),
            ["DueDate"]     = l.GetValueOrDefault("ShipDate") ?? h.GetValueOrDefault("DueDate"),   // 라인 납기 우선, 없으면 헤더
            ["Status"]      = status,
        };
    }

    private static decimal Dec(object? v) => v is null ? 0m : Convert.ToDecimal(v);

    private static Dictionary<string, object?>? ReadExisting(SqlConnection c, SqlTransaction tx, object? docEntry, object? lineNum)
    {
        using var cmd = new SqlCommand($"SELECT {string.Join(", ", Cols)} FROM dbo.WH_PurchaseOrder WHERE B1DocEntry = @E AND B1LineNum = @L",
            c, tx) { CommandTimeout = 30 };
        ColumnParams.Add(cmd, "@E", "WH_PurchaseOrder.B1DocEntry", docEntry);
        ColumnParams.Add(cmd, "@L", "WH_PurchaseOrder.B1LineNum", lineNum);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < r.FieldCount; i++) d[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
        return d;
    }

    private static int Merge(SqlConnection c, SqlTransaction tx, object? docEntry, object? lineNum, IReadOnlyDictionary<string, object?> v)
    {
        using var cmd = new SqlCommand { Connection = c, Transaction = tx, CommandTimeout = 30 };
        ColumnParams.Add(cmd, "@E", "WH_PurchaseOrder.B1DocEntry", docEntry);
        ColumnParams.Add(cmd, "@L", "WH_PurchaseOrder.B1LineNum", lineNum);
        var sets = new List<string>(); var insCols = new List<string>(); var insVals = new List<string>();
        for (int i = 0; i < Cols.Length; i++)
        {
            ColumnParams.Add(cmd, "@p" + i, "WH_PurchaseOrder." + Cols[i], v[Cols[i]]);
            sets.Add($"{Cols[i]} = @p{i}"); insCols.Add(Cols[i]); insVals.Add("@p" + i);
        }
        cmd.CommandText = $"""
            MERGE dbo.WH_PurchaseOrder WITH (HOLDLOCK) AS t
            USING (SELECT @E AS E, @L AS L) AS src ON t.B1DocEntry = src.E AND t.B1LineNum = src.L
            WHEN MATCHED THEN UPDATE SET {string.Join(", ", sets)}, SapSyncedAt = SYSDATETIME(),
                 ModifiedBy = '{SapIfRules.Actor}', ModifiedTS = SYSDATETIME()
            WHEN NOT MATCHED THEN INSERT (B1DocEntry, B1LineNum, {string.Join(", ", insCols)}, SapSyncedAt, CreatedBy)
                 VALUES (@E, @L, {string.Join(", ", insVals)}, SYSDATETIME(), '{SapIfRules.Actor}');
            """;
        return cmd.ExecuteNonQuery();
    }

    private static int SetStatus(SqlConnection c, SqlTransaction tx, object? docEntry, string status)
    {
        using var cmd = new SqlCommand($"""
            UPDATE dbo.WH_PurchaseOrder
               SET Status = @S, SapSyncedAt = SYSDATETIME(), ModifiedBy = '{SapIfRules.Actor}', ModifiedTS = SYSDATETIME()
             WHERE B1DocEntry = @E AND ISNULL(Status, '') <> @S
            """, c, tx) { CommandTimeout = 30 };
        ColumnParams.Add(cmd, "@S", "WH_PurchaseOrder.Status", status);
        ColumnParams.Add(cmd, "@E", "WH_PurchaseOrder.B1DocEntry", docEntry);
        return cmd.ExecuteNonQuery();
    }
}
