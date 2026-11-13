using System.Diagnostics;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Services.SapIf;

/// <summary>
/// 마스터 수신 한 종류의 정의. EOS_IF.IF_MD_* → AMES MD_* (키 MERGE). 정의서 IF 목록·매핑 시트 기준.
/// <paramref name="Cols"/> 는 IF 테이블과 MD 테이블에서 이름이 같은 업무 컬럼(MES 전용 컬럼은 넣지 않아 건드리지 않는다).
/// </summary>
/// <summary>Simple = 헤더 한 행이 MD 한 행(키 MERGE) · Bom = 헤더 1행 + 라인 N행을 한 트랜잭션으로(M03).</summary>
public enum MasterKind { Simple, Bom }

/// <param name="SapFlag">대상 MD 테이블에 SAPFlag 컬럼이 있는가(MD_Bom 라인은 없다 — 헤더 MD_BomVersion 에 있다).</param>
public sealed record MasterSpec(
    string IfId, string Name, string EosTable, string AmesTable, string Key, string[] Cols,
    string DeactivateCol, object DeactivateValue, string[] DefaultTrueWhenNull,
    MasterKind Kind = MasterKind.Simple, bool SapFlag = true);

public sealed record SapInTarget(MasterSpec Spec, int IntervalMin, int Batch, bool DryRun);

public sealed record SapInRunResult(
    string IfId, bool Ok, int Read, int Applied, int Failed, bool DryRun, string? Error, string? Note = null);

/// <summary>구현된 수신 대상 — 처리 순서는 정의서 2.2(UoM → Item → BOM → 고객 → 공급사 → 로케이션)를 따른다.
/// PO·MRP(T04·T08)는 아직 구현 전이라 목록에 없다.</summary>
public static class SapInMasters
{
    public static readonly IReadOnlyList<MasterSpec> All =
    [
        new("IF-M01", "단위 마스터", "IF_MD_UOM", "MD_Uom", "UOMCode",
            ["UOMName", "UOMCategory", "BaseFlag", "BaseUOM", "ConvFactor", "ActiveFlag"],
            "ActiveFlag", false, ["ActiveFlag"]),
        new("IF-M02", "품목 마스터", "IF_MD_ITEM", "MD_Item", "ItemNo",
            ["ItemName", "ItemNameEN", "ItemType", "ItemCategory", "InjFlag", "DefaultUOM", "RoutingType",
             "MinStock", "MaxStock", "SafetyStock", "UnitCost", "PalletQty", "ActiveFlag", "LeadTimeDays", "ProcureType"],
            "ActiveFlag", false, ["ActiveFlag"]),
        new("IF-M03", "BOM(헤더+라인)", "IF_MD_BOM_HDR", "MD_BomVersion", "VersionID",
            SapInBom.Version.Cols, "Status", "EXPIRED", [], MasterKind.Bom),
        new("IF-M04", "고객 마스터", "IF_MD_CUSTOMER", "MD_Customer", "CustomerID",
            ["CustomerCode", "CustomerName", "CustomerNameEn", "CustomerType", "BizRegNo", "Country",
             "ContactPerson", "ContactPhone", "ContactEmail", "CurrencyCode", "Status"],
            "Status", "CLOSED", []),
        new("IF-M05", "공급사 마스터", "IF_MD_VENDOR", "MD_Vendor", "VendorID",
            ["VendorName", "VendorType", "BizRegNo", "ContactPerson", "Phone", "Email", "PaymentTerms", "ActiveFlag"],
            "ActiveFlag", false, ["ActiveFlag"]),
        new("IF-M06", "창고(1차 로케이션)", "IF_MD_LOCATION", "MD_Location", "LocationID",
            ["LocationName", "LocationType", "PlantCode", "ActiveFlag", "WhCode", "AreaCode", "ParentLocationID", "LocationLevel"],
            "ActiveFlag", false, ["ActiveFlag"]),
    ];
}

/// <summary>
/// EOS_IF 의 N 행을 IF_SEQ 순으로 읽어 MD_* 에 반영한다(정의서 2.2). 행마다 AMES 로컬 트랜잭션으로 MERGE 하고
/// 커밋한 뒤에 EOS_IF 에 Y/E 를 되쓴다 — 원격 쿼리와 로컬 트랜잭션을 한 트랜잭션으로 묶지 않는다.
/// 되쓰기가 실패해도 MERGE 가 멱등이라 다음 주기에 다시 처리해도 안전하다.
/// DryRun 이면 MD_* 반영을 롤백하고 EOS_IF 에는 아무것도 쓰지 않는다.
/// </summary>
public sealed class SapInRunner(AmesConnectionFactory ames, EosIfConnectionFactory eos, SapIfLogRepository log)
{
    public SapInRunResult Run(SapInTarget target, CancellationToken ct = default)
    {
        var s = target.Spec;
        var runId = Guid.NewGuid();
        var sw = Stopwatch.StartNew();
        try
        {
            using var eosConn = eos.OpenConnection();

            var blocker = FirstBlockingPredecessor(eosConn, s);
            if (blocker is not null)
            {
                var msg = $"선행 {blocker.Value.IfId} 에 오류(E) {blocker.Value.Count}건 — 다음 주기로 미룸";
                log.Write(runId, s.IfId, "IN", null, null, null, "SKIP", msg);
                return new SapInRunResult(s.IfId, true, 0, 0, 0, target.DryRun, null, msg);
            }

            var rows = ReadPending(eosConn, s, target.Batch);
            int applied = 0, failed = 0, created = 0, changed = 0, same = 0;
            foreach (var row in rows)
            {
                ct.ThrowIfCancellationRequested();
                var seq    = (long)row["IF_SEQ"]!;
                var action = row["IF_ACTION"] as string;
                var key    = row[s.Key]?.ToString();
                try
                {
                    // M03 은 헤더 1행 + 라인 N행(HDR_SEQ) — 라인은 AMES 트랜잭션을 열기 전에 EOS_IF 에서 읽는다.
                    var children = s.Kind == MasterKind.Bom ? ReadChildren(eosConn, SapInBom.LineEosTable, seq) : null;
                    var (affected, kind, note) = ApplyOne(s, row, children, target.DryRun);
                    if (!target.DryRun) WriteBack(eosConn, s, seq, 'Y', null);
                    applied++;
                    if (kind == 'N') created++; else if (kind == 'C') changed++; else same++;
                    log.Write(runId, s.IfId, "IN", key, seq, action, "OK",
                        (target.DryRun ? "DRYRUN(반영 롤백) " : "") + note, affected, (int)sw.ElapsedMilliseconds);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failed++;
                    var msg = SapIfRules.ErrorMessage(ex);
                    if (!target.DryRun)
                    {
                        try { WriteBack(eosConn, s, seq, 'E', msg); }
                        catch (Exception wb) { msg += " | 되쓰기 실패: " + SapIfRules.ErrorMessage(wb); }
                    }
                    log.Write(runId, s.IfId, "IN", key, seq, action, "FAIL", msg, null, (int)sw.ElapsedMilliseconds);
                }
            }

            // 정책 3: AMES 에만 있는 행은 그대로 둔다 — 지우거나 비활성화하지 않고 목록만 남긴다.
            int amesOnly = 0;
            if (rows.Count > 0)
            {
                try
                {
                    var only = AmesOnlyKeysFor(eosConn, s);
                    amesOnly = only.Count;
                    if (only.Count > 0)
                        log.Write(runId, s.IfId, "IN", "AMES-ONLY", null, null, "SKIP",
                            SapIfRules.Truncate($"AMES 에만 있음(그대로 둠) {only.Count}건: {string.Join(", ", only.Take(50))}{(only.Count > 50 ? " …" : "")}", 1000));
                }
                catch (Exception ex) { log.Write(runId, s.IfId, "IN", "AMES-ONLY", null, null, "WARN", "AMES 단독 목록 조회 실패: " + SapIfRules.ErrorMessage(ex)); }
            }

            var summary = $"신규 {created}·변경 {changed}·동일 {same}·AMES 단독(유지) {amesOnly}";
            return new SapInRunResult(s.IfId, failed == 0, rows.Count, applied, failed, target.DryRun,
                failed == 0 ? null : $"{failed}건 반영 실패(EOS_IF 에 E 기록)", summary);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var msg = SapIfRules.ErrorMessage(ex);
            log.Write(runId, s.IfId, "IN", null, null, null, "FAIL", "수신 실행 실패: " + msg, null, (int)sw.ElapsedMilliseconds);
            return new SapInRunResult(s.IfId, false, 0, 0, 0, target.DryRun, msg);
        }
    }

    // ── EOS_IF 읽기·되쓰기 ────────────────────────────────────────────

    /// <summary>앞선 IF 에 E 행이 남아 있으면 그 IF(참조 무결성 — 선행 실패 시 후행은 다음 주기로).</summary>
    private static (string IfId, int Count)? FirstBlockingPredecessor(SqlConnection conn, MasterSpec s)
    {
        foreach (var p in SapInMasters.All)
        {
            if (p.IfId == s.IfId) break;
            using var cmd = new SqlCommand($"SELECT COUNT(*) FROM dbo.{p.EosTable} WHERE IF_STATUS = 'E'", conn) { CommandTimeout = 20 };
            var n = (int)cmd.ExecuteScalar()!;
            if (n > 0) return (p.IfId, n);
        }
        return null;
    }

    private static List<Dictionary<string, object?>> ReadPending(SqlConnection conn, MasterSpec s, int batch)
    {
        var list = new List<Dictionary<string, object?>>();
        using var cmd = new SqlCommand(
            $"SELECT TOP (@N) * FROM dbo.{s.EosTable} WITH (READPAST) WHERE IF_STATUS = 'N' ORDER BY IF_SEQ", conn)
        { CommandTimeout = 30 };
        cmd.Parameters.Add("@N", System.Data.SqlDbType.Int).Value = Math.Max(1, batch);
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
        {
            var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < rdr.FieldCount; i++)
                d[rdr.GetName(i)] = rdr.IsDBNull(i) ? null : rdr.GetValue(i);
            list.Add(d);
        }
        return list;
    }

    /// <summary>헤더(HDR_SEQ)에 딸린 라인 행(IF_SEQ 순).</summary>
    private static List<Dictionary<string, object?>> ReadChildren(SqlConnection conn, string table, long hdrSeq)
    {
        var list = new List<Dictionary<string, object?>>();
        using var cmd = new SqlCommand($"SELECT * FROM dbo.{table} WHERE HDR_SEQ = @H ORDER BY IF_SEQ", conn) { CommandTimeout = 30 };
        cmd.Parameters.Add("@H", System.Data.SqlDbType.BigInt).Value = hdrSeq;
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
        {
            var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < rdr.FieldCount; i++)
                d[rdr.GetName(i)] = rdr.IsDBNull(i) ? null : rdr.GetValue(i);
            list.Add(d);
        }
        return list;
    }

    private static void WriteBack(SqlConnection conn, MasterSpec s, long seq, char status, string? msg)
    {
        using var cmd = new SqlCommand($"""
            UPDATE dbo.{s.EosTable}
               SET IF_STATUS = @S, IF_MSG = @M, PROC_TS = SYSDATETIME()
             WHERE IF_SEQ = @Q AND IF_STATUS = 'N'
            """, conn) { CommandTimeout = 20 };
        cmd.Parameters.Add("@S", System.Data.SqlDbType.Char, 1).Value = status.ToString();
        cmd.Parameters.Add("@M", System.Data.SqlDbType.NVarChar, 1000).Value = (object?)SapIfRules.Truncate(msg, SapIfRules.MsgMax) ?? DBNull.Value;
        cmd.Parameters.Add("@Q", System.Data.SqlDbType.BigInt).Value = seq;
        cmd.ExecuteNonQuery();
    }

    // ── AMES 반영 ────────────────────────────────────────────────────

    /// <summary>AMES 의 키 목록과 EOS_IF 의 키 목록(상태 무관 전체)을 비교해 AMES 에만 있는 키를 돌려준다.</summary>
    private IReadOnlyList<string> AmesOnlyKeysFor(SqlConnection eosConn, MasterSpec s)
    {
        var sapKeys = new List<string>();
        using (var c = new SqlCommand($"SELECT DISTINCT {s.Key} FROM dbo.{s.EosTable}", eosConn) { CommandTimeout = 30 })
        using (var r = c.ExecuteReader())
            while (r.Read()) if (!r.IsDBNull(0)) sapKeys.Add(r.GetValue(0).ToString()!);

        var amesKeys = new List<string>();
        using var conn = ames.OpenConnection();
        using (var c = new SqlCommand($"SELECT {s.Key} FROM dbo.{s.AmesTable}", conn) { CommandTimeout = 30 })
        using (var r = c.ExecuteReader())
            while (r.Read()) if (!r.IsDBNull(0)) amesKeys.Add(r.GetValue(0).ToString()!);

        return SapIfRules.AmesOnlyKeys(amesKeys, sapKeys);
    }

    /// <summary>
    /// 한 행을 AMES 로컬 트랜잭션으로 반영하고 커밋(DryRun 이면 롤백).
    /// 반영 전 AMES 값을 읽어 신규(N)·변경(C)·동일(S)을 가리고 변경 전·후 값을 메모로 돌려준다(정책 1·2: SAP 우선).
    /// </summary>
    private (int Affected, char Kind, string Note) ApplyOne(MasterSpec s, Dictionary<string, object?> row,
        IReadOnlyList<Dictionary<string, object?>>? children, bool dryRun)
    {
        using var conn = ames.OpenConnection();
        using var tx = conn.BeginTransaction();
        try
        {
            (int Affected, char Kind, string Note) res;
            if (s.Kind == MasterKind.Bom)
                res = SapInBom.Apply(conn, tx, row, children ?? []);
            else
            {
                var norm = NormalizeRow(s, row);
                var (kind, note) = Compare(conn, tx, s, norm);
                int affected = Apply(conn, tx, s, norm);
                // 품목 기본 단위가 바뀌면 그 품목을 구성품으로 쓰는 BOM 라인 단위도 따라간다(MD-003 규칙과 같음).
                if (s.AmesTable == "MD_Item" && kind == 'C' && note.Contains("DefaultUOM:"))
                    note += $" · BOM 라인 단위 {SyncBomUom(conn, tx, norm)}건 동기화";
                res = (affected, kind, note);
            }
            if (dryRun) tx.Rollback(); else tx.Commit();
            return res;
        }
        catch { try { tx.Rollback(); } catch { } throw; }
    }

    /// <summary>MES 규칙을 SAP 행에도 적용한다 — 사출품 여부(InjFlag)는 품목 유형이 SUB 일 때만 1 이 될 수 있다(MD-003).</summary>
    internal static IReadOnlyDictionary<string, object?> NormalizeRow(MasterSpec s, IReadOnlyDictionary<string, object?> row)
    {
        if (s.AmesTable != "MD_Item") return row;
        var copy = new Dictionary<string, object?>(row, StringComparer.OrdinalIgnoreCase);
        if (!string.Equals(copy.GetValueOrDefault("ItemType") as string, "SUB", StringComparison.OrdinalIgnoreCase))
            copy["InjFlag"] = false;
        return copy;
    }

    private static int SyncBomUom(SqlConnection conn, SqlTransaction tx, IReadOnlyDictionary<string, object?> item)
    {
        using var cmd = new SqlCommand($"""
            UPDATE dbo.MD_Bom SET UOM = @U, ModifiedBy = '{SapIfRules.Actor}', ModifiedTS = SYSDATETIME()
             WHERE CompItemNo = @I AND ISNULL(UOM, '') <> ISNULL(@U, '')
            """, conn, tx) { CommandTimeout = 30 };
        ColumnParams.Add(cmd, "@U", "MD_Bom.UOM", item.GetValueOrDefault("DefaultUOM"));
        ColumnParams.Add(cmd, "@I", "MD_Bom.CompItemNo", item["ItemNo"]);
        return cmd.ExecuteNonQuery();
    }

    internal static (char Kind, string Note) Compare(SqlConnection conn, SqlTransaction tx, MasterSpec s,
        IReadOnlyDictionary<string, object?> row)
    {
        if (SapIfRules.IsDeactivate(row.TryGetValue("IF_ACTION", out var a) ? a as string : null))
            return ('C', $"비활성 처리({s.DeactivateCol} → {s.DeactivateValue})");

        var sapCol = s.SapFlag ? ", SAPFlag" : "";
        using var cmd = new SqlCommand($"SELECT {string.Join(", ", s.Cols)}{sapCol} FROM dbo.{s.AmesTable} WHERE {s.Key} = @K",
            conn, tx) { CommandTimeout = 30 };
        ColumnParams.Add(cmd, "@K", $"{s.AmesTable}.{s.Key}", row[s.Key]);
        var existing = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        using (var r = cmd.ExecuteReader())
        {
            if (!r.Read()) return ('N', "신규 등록(SAP 값)");
            for (int i = 0; i < r.FieldCount; i++) existing[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
        }

        var diffs = SapIfRules.Diff(existing, row, s.Cols, s.DefaultTrueWhenNull);
        bool fromMes = existing.TryGetValue("SAPFlag", out var f) && f is bool b && !b;
        var tail = fromMes ? " ※ MES 관리 행 → SAP 행으로 전환" : "";
        return diffs.Count == 0
            ? ('S', "동일(변경 없음)" + tail)
            : ('C', "변경(SAP 우선) " + SapIfRules.FormatDiff(diffs) + tail);
    }

    internal static int Apply(SqlConnection conn, SqlTransaction tx, MasterSpec s, IReadOnlyDictionary<string, object?> row)
    {
        var action = row.TryGetValue("IF_ACTION", out var a) ? a as string : null;
        string Col(string c) => $"{s.AmesTable}.{c}";

        using var cmd = new SqlCommand { Connection = conn, Transaction = tx, CommandTimeout = 30 };
        ColumnParams.Add(cmd, "@K", Col(s.Key), row[s.Key]);

        // D = 삭제가 아니라 비활성. 없는 키는 아무것도 하지 않는다(IF 에 없는 컬럼과 다른 업무 값은 건드리지 않음).
        if (SapIfRules.IsDeactivate(action))
        {
            ColumnParams.Add(cmd, "@D", Col(s.DeactivateCol), s.DeactivateValue);
            cmd.CommandText = $"""
                UPDATE dbo.{s.AmesTable}
                   SET {s.DeactivateCol} = @D{(s.SapFlag ? ", SAPFlag = 1" : "")}, ModifiedBy = '{SapIfRules.Actor}', ModifiedTS = SYSDATETIME()
                 WHERE {s.Key} = @K
                """;
            return cmd.ExecuteNonQuery();
        }

        var sets = new List<string>();
        var insCols = new List<string> { s.Key };
        var insVals = new List<string> { "@K" };
        for (int i = 0; i < s.Cols.Length; i++)
        {
            var c = s.Cols[i];
            var v = row.TryGetValue(c, out var raw) ? raw : null;
            if (v is null && s.DefaultTrueWhenNull.Contains(c)) v = true;
            var p = "@p" + i;
            ColumnParams.Add(cmd, p, Col(c), v);
            sets.Add($"{c} = {p}");
            insCols.Add(c);
            insVals.Add(p);
        }

        cmd.CommandText = $"""
            MERGE dbo.{s.AmesTable} WITH (HOLDLOCK) AS t
            USING (SELECT @K AS K) AS src ON t.{s.Key} = src.K
            WHEN MATCHED THEN UPDATE SET {string.Join(", ", sets)},
                 {(s.SapFlag ? "SAPFlag = 1, " : "")}ModifiedBy = '{SapIfRules.Actor}', ModifiedTS = SYSDATETIME()
            WHEN NOT MATCHED THEN INSERT ({string.Join(", ", insCols)}{(s.SapFlag ? ", SAPFlag" : "")}, CreatedBy)
                 VALUES ({string.Join(", ", insVals)}{(s.SapFlag ? ", 1" : "")}, '{SapIfRules.Actor}');
            """;
        return cmd.ExecuteNonQuery();
    }
}
