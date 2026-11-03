namespace AMES.Data.Services.DemandPlan;

/// <summary>
/// 수주(PO)·일별 발주(MM31007)·일별 구매계획(MM30011)·주간 예측(ZMM30004) 의 대조 규칙. 순수 함수 — 정본 테스트 DemandReconciliationTests.
/// 같은 수요를 가리키는 값은 같아야 한다. 한쪽만 있거나 값이 다르면 그 칸을 이슈로 만든다(조용히 고치지 않는다).
/// </summary>
public static class DemandReconciliation
{
    public const string KindPoVsDailyPo   = "PO_DAILYPO";     // 수주 발주량 ≠ 일별 발주량(MM31007)
    public const string KindDailyPoMissing = "DAILYPO_MISSING"; // 수주는 있는데 일별 발주(MM31007) 없음
    public const string KindPoMissing     = "PO_MISSING";     // 일별 발주(MM31007)는 있는데 수주 없음
    public const string KindDailyVsPlanPo = "DAILYPO_PLANPO"; // 일별 발주(MM31007) ≠ 구매계획 P/O(MM30011)

    /// <summary>품번 × 날짜 한 칸. null 은 그 출처에 행이 없다는 뜻(0 과 구분).</summary>
    public sealed record DayRow(string CustomerId, string Item, DateOnly Date, decimal? PoQty, decimal? DailyPoQty, decimal? PlanPoQty);

    public sealed record Issue(string CustomerId, string Item, DateOnly Date, string Kind, decimal Left, decimal Right, string Detail);

    public static List<Issue> Days(IEnumerable<DayRow> rows)
    {
        var issues = new List<Issue>();
        foreach (var r in rows.OrderBy(x => x.CustomerId, StringComparer.Ordinal).ThenBy(x => x.Item, StringComparer.Ordinal).ThenBy(x => x.Date))
        {
            if (r.PoQty is decimal po && r.DailyPoQty is decimal dp)
            {
                if (po != dp)
                    issues.Add(new Issue(r.CustomerId, r.Item, r.Date, KindPoVsDailyPo, po, dp, $"수주 {N(po)} ≠ 일별 발주 {N(dp)}"));
            }
            else if (r.PoQty is decimal po2 && po2 != 0 && r.DailyPoQty is null)
                issues.Add(new Issue(r.CustomerId, r.Item, r.Date, KindDailyPoMissing, po2, 0, $"수주 {N(po2)} 은 있으나 일별 발주(MM31007) 행이 없음"));
            else if (r.DailyPoQty is decimal dp2 && dp2 != 0 && r.PoQty is null)
                issues.Add(new Issue(r.CustomerId, r.Item, r.Date, KindPoMissing, 0, dp2, $"일별 발주 {N(dp2)} 은 있으나 수주(PO) 행이 없음"));

            if (r.DailyPoQty is decimal dq && r.PlanPoQty is decimal pq && dq != pq)
                issues.Add(new Issue(r.CustomerId, r.Item, r.Date, KindDailyVsPlanPo, dq, pq, $"일별 발주 {N(dq)} ≠ 구매계획 P/O {N(pq)}"));
        }
        return issues;
    }

    static string N(decimal v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
}
