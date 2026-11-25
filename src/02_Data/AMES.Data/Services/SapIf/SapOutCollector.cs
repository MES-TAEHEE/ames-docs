using System.Data;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Services.SapIf;

public sealed record SapCollectResult(int Issues, int Receipts, int Rejects, int Closes, int Cancels, string? Error)
{
    public int Total => Issues + Receipts + Rejects + Closes + Cancels;
}

/// <summary>
/// 현장에서 이미 확정된 기록(BOM 자동 차감·생산실적·불량·WO 상태)을 읽어 SAP 송신 아웃박스에 적재한다(11-23).
/// 확정 트랜잭션에 끼어들지 않고 SAPOUT Worker 가 주기적으로 한 번 훑는다 — 생산 경로를 건드리지 않고(생산이 먼저),
/// 실적이 한 개씩 올라오는 현장(GoodQty 1)을 B1 입고 문서 한 건씩 만들지 않도록 WO × 생산일 × 교대로 묶는다.
/// 대상은 IF-T01 생성(C)이 아웃박스에 있는 WO 뿐이다 — 연결 이전에 발행된 WO 는 B1 에 오더가 없어 보내지 않는다.
/// 같은 키는 한 번만 들어가므로(아웃박스 UNIQUE) 다시 훑어도 중복되지 않는다. 마지막으로 보낸 지점은 아웃박스 키 끝의 번호(마지막 ID)로 안다.
/// 키 형식(≤30자): 투입 I{WoID}-{마지막 거래ID} · 실적 R{WoID}-{yyMMdd}{교대}-{마지막 ResultID} · 불량 D{WoID}-{마지막 DefectID}.
/// </summary>
public sealed class SapOutCollector(AmesConnectionFactory f, SapIfLogRepository log)
{
    public const string IssueWhsKey = "SAP_IF_WO_ISSUE_WHSCODE";
    public const string RejectWhsKey = "SAP_IF_WO_REJECT_WHSCODE";
    /// <summary>방금 확정된 행은 아직 커밋 중일 수 있어 이만큼 지난 것만 묶는다(분).</summary>
    public const int SettleMinutes = 2;

    private sealed record Wo(int WoId, string Number, string ItemNo, decimal Qty, string? Status, DateTime? Due);

    public SapCollectResult Collect(CancellationToken ct = default)
    {
        int iss = 0, rec = 0, rej = 0, cls = 0, can = 0;
        var errors = new List<string>();
        var runId = Guid.NewGuid();
        try
        {
            using var conn = f.OpenConnection();
            var whs = ReadConfig(conn, SapOutHooks.WhsConfigKey) ?? SapOutHooks.DefaultWhsCode;
            var issueWhs = ReadConfig(conn, IssueWhsKey) ?? whs;
            var rejectWhs = ReadConfig(conn, RejectWhsKey);

            foreach (var wo in ReadEligible(conn))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    iss += CollectIssues(conn, runId, wo, issueWhs);
                    rec += CollectReceipts(conn, runId, wo, whs);
                    if (rejectWhs is not null) rej += CollectRejects(conn, runId, wo, whs, rejectWhs);
                    var (c, x) = CollectEnd(conn, runId, wo, whs);
                    cls += c; can += x;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    errors.Add($"{wo.Number}: {SapIfRules.ErrorMessage(ex)}");
                    log.Write(runId, "IF-T01", "OUT", wo.Number, null, null, "FAIL", "아웃박스 적재 실패: " + SapIfRules.ErrorMessage(ex));
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            errors.Add(SapIfRules.ErrorMessage(ex));
        }
        return new SapCollectResult(iss, rec, rej, cls, can, errors.Count == 0 ? null : SapIfRules.Truncate(string.Join(" | ", errors), 1000));
    }

    // ── 대상 WO: 생성(C)이 아웃박스에 있고 아직 종료(L)·취소(X)가 나가지 않은 것 ─────────────────
    private static List<Wo> ReadEligible(SqlConnection conn)
    {
        using var cmd = new SqlCommand("""
            SELECT TOP (500) w.WoID, w.WoNumber, w.ItemNo, w.OrderQty, w.Status, COALESCE(w.DueDate, w.ProdDeadline) AS Due
            FROM   dbo.PP_WorkOrder w
            WHERE  EXISTS (SELECT 1 FROM dbo.SAP_IfOutbox o WHERE o.IfID = 'IF-T01' AND o.Action = 'C' AND o.MesKey = w.WoNumber)
              AND  NOT EXISTS (SELECT 1 FROM dbo.SAP_IfOutbox o WHERE o.IfID = 'IF-T01' AND o.Action IN ('L', 'X') AND o.MesKey = w.WoNumber)
            ORDER  BY w.WoID;
            """, conn) { CommandTimeout = 30 };
        var list = new List<Wo>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new Wo((int)r["WoID"], (string)r["WoNumber"], (string)r["ItemNo"], Convert.ToDecimal(r["OrderQty"]),
                            r["Status"] as string, r["Due"] as DateTime?));
        return list;
    }

    private static string? ReadConfig(SqlConnection conn, string key)
    {
        using var cmd = new SqlCommand("""
            SELECT TOP 1 NULLIF(LTRIM(RTRIM(ConfigValue)), '') FROM dbo.SYS_Config
            WHERE ConfigKey = @Key AND ISNULL(IsActive, 1) = 1 ORDER BY ConfigID;
            """, conn);
        cmd.Parameters.Add("@Key", SqlDbType.VarChar, 60).Value = key;
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>이 키 접두어로 이미 적재한 마지막 번호(키 끝 숫자). 없으면 0.</summary>
    private static long Watermark(SqlConnection conn, string ifId, string prefix)
    {
        using var cmd = new SqlCommand("""
            SELECT ISNULL(MAX(TRY_CONVERT(bigint, RIGHT(MesKey, CHARINDEX('-', REVERSE(MesKey)) - 1))), 0)
            FROM   dbo.SAP_IfOutbox WHERE IfID = @If AND MesKey LIKE @Pat AND Action = 'C';
            """, conn);
        cmd.Parameters.Add("@If", SqlDbType.VarChar, 10).Value = ifId;
        cmd.Parameters.Add("@Pat", SqlDbType.VarChar, 40).Value = prefix + "-%";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static string? FinalProcess(SqlConnection conn, int woId)
    {
        using var cmd = new SqlCommand("SELECT TOP 1 ProcessCode FROM dbo.PP_WorkOrderRouting WHERE WoID = @Wo ORDER BY StepSeq DESC;", conn);
        cmd.Parameters.Add("@Wo", SqlDbType.Int).Value = woId;
        return cmd.ExecuteScalar() as string;
    }

    private void Enqueue(SqlConnection conn, Guid runId, string ifId, string key, string action, string json, string what)
    {
        if (SapIfOutboxRepository.Enqueue(conn, null, ifId, key, action, json) is not null)
            log.Write(runId, ifId, "OUT", key, null, action, "OK", "아웃박스 적재 — " + what);
    }

    // ── IF-T02 자재 투입: 그 WO 의 LOT 에서 BOM 자동 차감(BACKFLUSH)된 구성품 + 스캔해서 쓴 사출 코어(IMG_CORE)를 묶어서 ─────
    // 코어는 따로 센다(키 C…, 기준점도 따로): IMG 에서 코어를 스캔하면 ConsumeCore 가 IMG_CORE 거래로 코어 재고를 빼고 BOM 자동 차감은 그 코어를 건너뛰어
    // (이중 차감 방지) BACKFLUSH 에는 코어가 없다 — 그대로면 B1 완제품 생산오더의 코어 구성품이 투입 안 돼 B1 코어 재고가 실제보다 많이 남는다(11-24).
    // 기준점이 따로라 IMG_CORE 가 이미 쌓여 있던 WO 도 처음 수집 때 전부 한 번에 나간다.
    private int CollectIssues(SqlConnection conn, Guid runId, Wo wo, string whs)
        => CollectIssueKind(conn, runId, wo, whs, "I", "BACKFLUSH", "투입 구성품")
         + CollectIssueKind(conn, runId, wo, whs, "C", CoreUseSourceType, "스캔 코어 투입");

    /// <summary>코어가 그 WO 완제품 BOM 의 직접 구성품이다(별칭 t = 재고 거래, l = LOT). F2321 처럼 코어가 직접 자식이면 B1 생산오더에 코어 구성품이 있다.
    /// M0211·M0221 처럼 직접 자식이 서브조립(M2311·M2321)이면 코어는 그 아래라 서브조립이 BACKFLUSH 로 이미 투입돼 코어를 또 보내면 이중이다.</summary>
    private const string DirectComponentOfWoItem = """
        EXISTS (SELECT 1 FROM dbo.PP_WorkOrder wi JOIN dbo.MD_Bom b ON b.ParentItemNo = wi.ItemNo
                WHERE wi.WoID = l.WoID AND b.CompItemNo = t.PartNo AND ISNULL(b.ActiveFlag, 1) = 1)
        """;

    /// <summary>IMG 가 스캔한 사출 코어를 쓸 때 남기는 재고 거래 종류(PalletStockRepository.CoreUseSourceType 과 같다).</summary>
    internal const string CoreUseSourceType = "IMG_CORE";

    private int CollectIssueKind(SqlConnection conn, Guid runId, Wo wo, string whs, string keyPrefix, string sourceType, string what)
    {
        long wm = Watermark(conn, "IF-T02", $"{keyPrefix}{wo.WoId}");
        var parts = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        long max = wm; DateTime postDate = DateTime.MinValue;
        using (var cmd = new SqlCommand($"""
            SELECT t.TransactionID, t.PartNo, -t.QtyChange AS Qty, t.TransactionTime
            FROM   dbo.WH_InventoryTransaction t
            JOIN   dbo.tbl_Lot l ON l.LotID = t.SourceID
            WHERE  t.SourceType = @Src AND t.QtyChange < 0 AND l.WoID = @Wo
              AND  t.TransactionID > @Wm AND t.TransactionTime < DATEADD(MINUTE, -{SettleMinutes}, SYSDATETIME())
              AND  (@Src <> 'IMG_CORE' OR {DirectComponentOfWoItem})
            ORDER  BY t.TransactionID;
            """, conn) { CommandTimeout = 30 })
        {
            cmd.Parameters.Add("@Src", SqlDbType.VarChar, 30).Value = sourceType;
            cmd.Parameters.Add("@Wo", SqlDbType.Int).Value = wo.WoId;
            cmd.Parameters.Add("@Wm", SqlDbType.BigInt).Value = wm;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var part = (string)r["PartNo"];
                parts[part] = parts.GetValueOrDefault(part) + Convert.ToDecimal(r["Qty"]);
                max = Math.Max(max, Convert.ToInt64(r["TransactionID"]));
                var at = (DateTime)r["TransactionTime"]; if (at > postDate) postDate = at;
            }
        }
        if (max == wm || parts.Count == 0) return 0;

        var lines = parts.Where(p => p.Value > 0).OrderBy(p => p.Key)
            .Select(p => (IDictionary<string, object?>)new Dictionary<string, object?>
            { ["CompItemNo"] = p.Key, ["Qty"] = p.Value, ["WhsCode"] = whs, ["ExtraFlag"] = 0 }).ToList();
        if (lines.Count == 0) return 0;
        var key = $"{keyPrefix}{wo.WoId}-{max}";
        var hdr = new Dictionary<string, object?> { ["MES_ISSUE_NO"] = key, ["MES_WO_NO"] = wo.Number, ["PostDate"] = postDate.Date };
        Enqueue(conn, runId, "IF-T02", key, "C", SapOutPayload.ToJson(hdr, lines), $"{wo.Number} {what} {lines.Count}종");
        return 1;
    }

    // ── IF-T03 생산실적(양품): 마지막 공정의 실적을 WO × 생산일 × 교대로 묶어서 ─────────────────────
    private int CollectReceipts(SqlConnection conn, Guid runId, Wo wo, string whs)
    {
        var proc = FinalProcess(conn, wo.WoId);
        if (proc is null) return 0;
        long wm = Watermark(conn, "IF-T03", $"R{wo.WoId}");
        var groups = new SortedDictionary<(DateTime Day, string Shift), (decimal Qty, long Max, string? Line)>();
        using (var cmd = new SqlCommand($"""
            SELECT r.ResultID, r.GoodQty, r.ProdDate, ISNULL(r.ShiftCode, '') AS Shift, r.LineID
            FROM   dbo.PR_ProductionResult r
            WHERE  r.WoID = @Wo AND r.ProcessCode = @Proc AND r.GoodQty > 0 AND r.ResultID > @Wm
              AND  r.CreatedTS < DATEADD(MINUTE, -{SettleMinutes}, SYSDATETIME())
            ORDER  BY r.ResultID;
            """, conn) { CommandTimeout = 30 })
        {
            cmd.Parameters.Add("@Wo", SqlDbType.Int).Value = wo.WoId;
            cmd.Parameters.Add("@Proc", SqlDbType.VarChar, 10).Value = proc;
            cmd.Parameters.Add("@Wm", SqlDbType.BigInt).Value = wm;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var k = (((r["ProdDate"] as DateTime?) ?? DbClock.Today).Date, (string)r["Shift"]);
                var cur = groups.GetValueOrDefault(k);
                groups[k] = (cur.Qty + Convert.ToDecimal(r["GoodQty"]), Math.Max(cur.Max, Convert.ToInt64(r["ResultID"])), r["LineID"] as string ?? cur.Line);
            }
        }
        int n = 0;
        foreach (var (g, v) in groups)
        {
            var key = $"R{wo.WoId}-{g.Day:yyMMdd}{g.Shift}-{v.Max}";
            if (key.Length > 30) key = $"R{wo.WoId}-{g.Day:yyMMdd}-{v.Max}";
            var hdr = new Dictionary<string, object?>
            {
                ["MES_RESULT_NO"] = key, ["MES_WO_NO"] = wo.Number, ["ItemNo"] = wo.ItemNo,
                ["CompleteQty"] = v.Qty, ["RejectQty"] = 0m, ["WhsCode"] = whs,
                ["PostDate"] = g.Day, ["WorkDate"] = g.Day,
            };
            if (g.Shift.Length > 0) hdr["Shift"] = g.Shift;
            if (!string.IsNullOrEmpty(v.Line)) hdr["LineID"] = v.Line;
            Enqueue(conn, runId, "IF-T03", key, "C", SapOutPayload.ToJson(hdr), $"{wo.Number} {g.Day:yyyy-MM-dd} {g.Shift} 양품 {v.Qty:0.###}");
            n++;
        }
        return n;
    }

    // ── IF-T03 불량: 마지막 공정에서 등록된 불량 — 불량 창고 코드가 설정돼 있을 때만 ────────────────────
    private int CollectRejects(SqlConnection conn, Guid runId, Wo wo, string whs, string rejectWhs)
    {
        var proc = FinalProcess(conn, wo.WoId);
        if (proc is null) return 0;
        long wm = Watermark(conn, "IF-T03", $"D{wo.WoId}");
        decimal qty = 0; long max = wm; string? code = null; DateTime day = DateTime.MinValue;
        using (var cmd = new SqlCommand($"""
            SELECT d.DefectID, d.Qty, d.DefectCode, d.DetectedAt
            FROM   dbo.PR_DefectDetail d
            WHERE  d.WoID = @Wo AND d.ProcessCode = @Proc AND d.Qty > 0 AND d.DefectID > @Wm
              AND  d.CreatedTS < DATEADD(MINUTE, -{SettleMinutes}, SYSDATETIME())
            ORDER  BY d.DefectID;
            """, conn) { CommandTimeout = 30 })
        {
            cmd.Parameters.Add("@Wo", SqlDbType.Int).Value = wo.WoId;
            cmd.Parameters.Add("@Proc", SqlDbType.VarChar, 10).Value = proc;
            cmd.Parameters.Add("@Wm", SqlDbType.BigInt).Value = wm;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                qty += Convert.ToDecimal(r["Qty"]);
                max = Math.Max(max, Convert.ToInt64(r["DefectID"]));
                code ??= r["DefectCode"] as string;
                if (r["DetectedAt"] is DateTime at && at > day) day = at;
            }
        }
        if (qty <= 0 || max == wm) return 0;
        var key = $"D{wo.WoId}-{max}";
        var hdr = new Dictionary<string, object?>
        {
            ["MES_RESULT_NO"] = key, ["MES_WO_NO"] = wo.Number, ["ItemNo"] = wo.ItemNo,
            ["CompleteQty"] = 0m, ["RejectQty"] = qty, ["WhsCode"] = whs, ["RejectWhsCode"] = rejectWhs,
            ["PostDate"] = day == DateTime.MinValue ? DbClock.Today : day.Date,
        };
        if (!string.IsNullOrEmpty(code)) hdr["DefectCode"] = code;
        Enqueue(conn, runId, "IF-T03", key, "C", SapOutPayload.ToJson(hdr), $"{wo.Number} 불량 {qty:0.###}");
        return 1;
    }

    // ── IF-T01 종료(L)·취소(X) ───────────────────────────────────────────────────────────────────────
    private (int Closed, int Cancelled) CollectEnd(SqlConnection conn, Guid runId, Wo wo, string whs)
    {
        if (wo.Status is not ("Completed" or "Closed" or "Cancelled")) return (0, 0);
        // 방금 확정된 실적·투입이 아직 아웃박스에 안 들어갔다면 다음 주기에 종료한다(종료가 실적보다 먼저 나가면 B1 이 거부한다)
        if (HasUncollected(conn, wo)) return (0, 0);

        var hdr = new Dictionary<string, object?>
        {
            ["MES_WO_NO"] = wo.Number, ["ItemNo"] = wo.ItemNo, ["PlannedQty"] = wo.Qty, ["WhsCode"] = whs,
            ["DueDate"] = wo.Due ?? DbClock.Today,
        };
        if (wo.Status == "Cancelled")
        {
            // 취소(X)는 B1 에서 실적이 없을 때만 된다 — 투입·실적이 한 건이라도 나갔으면 사람이 B1 에서 처리해야 한다
            using var chk = new SqlCommand("""
                SELECT COUNT(*) FROM dbo.SAP_IfOutbox
                WHERE IfID IN ('IF-T02','IF-T03') AND JSON_VALUE(PayloadJson, '$.hdr.MES_WO_NO') = @Wo;
                """, conn);
            chk.Parameters.Add("@Wo", SqlDbType.VarChar, 30).Value = wo.Number;
            if (Convert.ToInt32(chk.ExecuteScalar()) > 0)
            {
                log.Write(runId, "IF-T01", "OUT", wo.Number, null, "X", "SKIP", "취소(X) 보류 — 이미 투입·실적이 SAP 로 나갔다. B1 에서 직접 처리 필요");
                return (0, 0);
            }
            Enqueue(conn, runId, "IF-T01", wo.Number, "X", SapOutPayload.ToJson(hdr), $"{wo.Number} 취소");
            return (0, 1);
        }
        hdr["CloseDate"] = DbClock.Today;
        Enqueue(conn, runId, "IF-T01", wo.Number, "L", SapOutPayload.ToJson(hdr), $"{wo.Number} 종료");
        return (1, 0);
    }

    private static bool HasUncollected(SqlConnection conn, Wo wo)
    {
        using var cmd = new SqlCommand($"""
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM dbo.WH_InventoryTransaction t JOIN dbo.tbl_Lot l ON l.LotID = t.SourceID
                WHERE t.SourceType = 'BACKFLUSH' AND t.QtyChange < 0 AND l.WoID = @Wo AND t.TransactionID > @Wi)
              OR EXISTS (
                SELECT 1 FROM dbo.WH_InventoryTransaction t JOIN dbo.tbl_Lot l ON l.LotID = t.SourceID
                WHERE t.SourceType = 'IMG_CORE' AND t.QtyChange < 0 AND l.WoID = @Wo AND t.TransactionID > @Wc AND {DirectComponentOfWoItem})
              OR EXISTS (
                SELECT 1 FROM dbo.PR_ProductionResult r
                WHERE r.WoID = @Wo AND r.GoodQty > 0 AND r.ResultID > @Wr
                  AND r.ProcessCode = (SELECT TOP 1 ProcessCode FROM dbo.PP_WorkOrderRouting WHERE WoID = @Wo ORDER BY StepSeq DESC))
            THEN 1 ELSE 0 END;
            """, conn) { CommandTimeout = 30 };
        cmd.Parameters.Add("@Wo", SqlDbType.Int).Value = wo.WoId;
        cmd.Parameters.Add("@Wi", SqlDbType.BigInt).Value = Watermark(conn, "IF-T02", $"I{wo.WoId}");
        cmd.Parameters.Add("@Wc", SqlDbType.BigInt).Value = Watermark(conn, "IF-T02", $"C{wo.WoId}");
        cmd.Parameters.Add("@Wr", SqlDbType.BigInt).Value = Watermark(conn, "IF-T03", $"R{wo.WoId}");
        return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
    }
}
