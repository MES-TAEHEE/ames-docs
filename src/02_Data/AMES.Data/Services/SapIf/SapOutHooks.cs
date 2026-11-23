using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Services.SapIf;

/// <summary>
/// 현장·계획 이벤트가 생길 때 SAP 송신 아웃박스(SAP_IfOutbox)에 한 건 넣는 연결 지점(11-23).
/// 호출자의 AMES 트랜잭션 안에서 부른다 — EOS_IF 에는 쓰지 않으므로 SAP 쪽 지연이 현장을 막지 않는다.
/// 세이브포인트로 감싸 아웃박스 쓰기가 실패해도(테이블 없음 등) 본 작업(WO 발행)은 그대로 진행한다.
/// 실제 송신은 AMES.Api 의 SAPOUT Worker 가 순서대로(생성 → 투입 → 실적 → 종료) 하며 SW_SAPOUT.DRYRUN=1 이면 EOS_IF 에 쓰지 않는다.
/// </summary>
public static class SapOutHooks
{
    /// <summary>B1 에 입고할 완제품/반제품 창고 코드(IF-T01 WhsCode). SYS_Config 키 — 없으면 <see cref="DefaultWhsCode"/>.</summary>
    public const string WhsConfigKey = "SAP_IF_WO_WHSCODE";
    public const string DefaultWhsCode = "W1MN";

    /// <summary>WO 가 발행(Released)됐다 → IF-T01 생성(C) 한 건. 이미 있으면 아무것도 하지 않는다(같은 키 1회).</summary>
    /// <returns>아웃박스에 새로 넣었으면 true.</returns>
    public static bool WoReleased(SqlConnection conn, SqlTransaction tx, int woId)
    {
        const string save = "sapout_wo";
        try
        {
            tx.Save(save);
            var p = ReadWo(conn, tx, woId);
            if (p is null) return false;

            var hdr = new Dictionary<string, object?>
            {
                ["MES_WO_NO"] = p.WoNumber,
                ["ItemNo"] = p.ItemNo,
                ["PlannedQty"] = p.Qty,
                ["WhsCode"] = p.Whs,
                ["StartDate"] = p.Today,
                ["DueDate"] = p.Due ?? p.Today,
                ["PostDate"] = p.Today,
            };
            if (!string.IsNullOrEmpty(p.LineId)) hdr["LineID"] = p.LineId;
            if (!string.IsNullOrEmpty(p.SoRef)) hdr["MES_SO_NO"] = p.SoRef;

            var id = SapIfOutboxRepository.Enqueue(conn, tx, "IF-T01", p.WoNumber, "C", SapOutPayload.ToJson(hdr));
            return id is not null;
        }
        catch
        {
            // 송신 준비 실패가 생산 계획을 막으면 안 된다 — 이 호출이 한 일만 되돌리고 계속한다.
            try { tx.Rollback(save); } catch { /* 트랜잭션이 이미 못 쓰는 상태면 호출자의 다음 문장이 실패한다 */ }
            return false;
        }
    }

    private sealed record WoInfo(string WoNumber, string ItemNo, decimal Qty, DateTime? Due, DateTime Today,
        string? LineId, string? SoRef, string Whs);

    private static WoInfo? ReadWo(SqlConnection conn, SqlTransaction tx, int woId)
    {
        using var cmd = new SqlCommand("""
            SELECT w.WoNumber, w.ItemNo, w.OrderQty, COALESCE(w.DueDate, w.ProdDeadline) AS Due,
                   CAST(SYSDATETIME() AS date) AS Today,
                   (SELECT TOP 1 r.LineID FROM dbo.PP_WorkOrderRouting r
                     WHERE r.WoID = w.WoID AND r.LineID IS NOT NULL ORDER BY r.StepSeq DESC) AS LineID,
                   CASE WHEN so.SoNumber IS NULL THEN NULL
                        ELSE LEFT(so.SoNumber + ISNULL('-' + CAST(so.SoLineNo AS varchar(10)), ''), 30) END AS SoRef,
                   (SELECT TOP 1 NULLIF(LTRIM(RTRIM(c.ConfigValue)), '') FROM dbo.SYS_Config c
                     WHERE c.ConfigKey = @Key AND ISNULL(c.IsActive, 1) = 1 ORDER BY c.ConfigID) AS Whs
            FROM   dbo.PP_WorkOrder w
            LEFT JOIN dbo.PP_CustomerOrder so ON so.SoID = w.SoID
            WHERE  w.WoID = @WoID;
            """, conn, tx);
        cmd.Parameters.Add("@WoID", System.Data.SqlDbType.Int).Value = woId;
        cmd.Parameters.Add("@Key", System.Data.SqlDbType.VarChar, 60).Value = WhsConfigKey;
        using var r = cmd.ExecuteReader();
        if (!r.Read() || r["WoNumber"] is not string wo || string.IsNullOrWhiteSpace(wo)) return null;
        return new WoInfo(wo, (string)r["ItemNo"], Convert.ToDecimal(r["OrderQty"]),
            r["Due"] as DateTime?, (DateTime)r["Today"], r["LineID"] as string, r["SoRef"] as string,
            r["Whs"] as string ?? DefaultWhsCode);
    }
}
