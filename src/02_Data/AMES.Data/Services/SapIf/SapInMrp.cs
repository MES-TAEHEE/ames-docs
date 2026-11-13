using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Services.SapIf;

/// <summary>
/// IF-T08 자재 소요계획 수신: EOS_IF.IF_TX_MRP_PLAN_HDR 1행 + IF_TX_MRP_PLAN_LINE N행(품목 × 공급사 × 기간 스냅샷) → <c>SCM_MrpPlan</c>.
/// 정의서: 헤더 단위로 계획 데이터를 통째로 교체한다 — 같은 버킷(D 일별 / W 주별)의 기존 행을 지우고 이번 스냅샷으로 채운다.
/// 가정: 헤더 IF_ACTION='X'(이전 스냅샷 무효 표시)는 새 스냅샷(C)이 이미 교체했으므로 아무것도 하지 않는다(새 스냅샷을 지우지 않기 위해).
/// 공급사 포털 화면은 후속이며 이 테이블이 원천이다.
/// </summary>
internal static class SapInMrp
{
    public const string LineEosTable = "IF_TX_MRP_PLAN_LINE";

    public static (int Affected, char Kind, string Note) Apply(SqlConnection conn, SqlTransaction tx,
        IReadOnlyDictionary<string, object?> h, IReadOnlyList<Dictionary<string, object?>> lines)
    {
        var action = h.GetValueOrDefault("IF_ACTION") as string;
        var bucket = h["BUCKET"];
        if (string.Equals(action, "X", StringComparison.Ordinal))
            return (0, 'S', "이전 스냅샷 무효 표시 — 새 스냅샷이 이미 교체해 변경 없음");

        int removed;
        using (var del = new SqlCommand("DELETE FROM dbo.SCM_MrpPlan WHERE Bucket = @B", conn, tx) { CommandTimeout = 60 })
        {
            ColumnParams.Add(del, "@B", "SCM_MrpPlan.Bucket", bucket);
            removed = del.ExecuteNonQuery();
        }

        // 한 문장·매개변수 재사용으로 라인 수만큼 넣는다.
        using var ins = new SqlCommand($"""
            INSERT INTO dbo.SCM_MrpPlan
                (EosHdrSeq, PlanDate, Bucket, HorizonFrom, HorizonTo, Scenario, RunTS, ItemNo, VendorID, PeriodStart, PeriodLabel,
                 OpeningQty, GrossReqQty, ScheduledQty, PlannedQty, PackQty, UOM, OrderedQty, CreatedBy)
            VALUES (@Seq, @PD, @B, @HF, @HT, @Sc, @Run, @Item, @Ven, @PS, @PLbl, @Op, @Gr, @Sch, @Plnd, @Pk, @U, @Ord, '{SapIfRules.Actor}')
            """, conn, tx) { CommandTimeout = 60 };
        void P(string n, string col, object? v) => ColumnParams.Add(ins, n, "SCM_MrpPlan." + col, v);
        P("@Seq", "EosHdrSeq", h["IF_SEQ"]);  P("@PD", "PlanDate", h["PLAN_DATE"]);  P("@B", "Bucket", bucket);
        P("@HF", "HorizonFrom", h["HORIZON_FROM"]);  P("@HT", "HorizonTo", h["HORIZON_TO"]);
        P("@Sc", "Scenario", h.GetValueOrDefault("SCENARIO"));  P("@Run", "RunTS", h["RUN_TS"]);
        foreach (var (n, c) in new[] { ("@Item", "ItemNo"), ("@Ven", "VendorID"), ("@PS", "PeriodStart"), ("@PLbl", "PeriodLabel"),
                                       ("@Op", "OpeningQty"), ("@Gr", "GrossReqQty"), ("@Sch", "ScheduledQty"), ("@Plnd", "PlannedQty"),
                                       ("@Pk", "PackQty"), ("@U", "UOM"), ("@Ord", "OrderedQty") })
            P(n, c, null);

        int inserted = 0;
        foreach (var l in lines)
        {
            Set(ins, "@Item", l["ItemNo"]);            Set(ins, "@Ven", l.GetValueOrDefault("VendorID"));
            Set(ins, "@PS", l["PeriodStart"]);         Set(ins, "@PLbl", l.GetValueOrDefault("PeriodLabel"));
            Set(ins, "@Op", l.GetValueOrDefault("OpeningQty"));   Set(ins, "@Gr", l["GrossReqQty"]);
            Set(ins, "@Sch", l["ScheduledQty"]);       Set(ins, "@Plnd", l["PlannedQty"]);
            Set(ins, "@Pk", l.GetValueOrDefault("PackQty"));      Set(ins, "@U", l.GetValueOrDefault("UOM"));
            Set(ins, "@Ord", l.GetValueOrDefault("OrderedQty"));
            inserted += ins.ExecuteNonQuery();
        }

        var pd = h["PLAN_DATE"] is DateTime d ? d.ToString("yyyy-MM-dd") : h["PLAN_DATE"]?.ToString();
        var note = $"MRP 스냅샷 교체(기준일 {pd}, 버킷 {bucket}): 기존 {removed}행 삭제 → {inserted}행 등록";
        return (removed + inserted, removed == 0 ? 'N' : 'C', note);
    }

    private static void Set(SqlCommand cmd, string name, object? value)
        => cmd.Parameters[name].Value = value ?? DBNull.Value;
}
