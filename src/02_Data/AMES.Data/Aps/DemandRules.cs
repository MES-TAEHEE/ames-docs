using System.Globalization;
using AMES.Data.Aps.Domain;

namespace AMES.Data.Aps;

/// <summary>
/// PP_CustomerOrder 행 → 품번 × 일자 수요 (스펙 §5). 순수 함수, DB 없음. 정본 테스트 DemandRulesTests.
/// ① 상태 Confirmed(+IncludeOpen 이면 Open), 고객 필터  ② 잔량 = OrderQty − ShippedQty ≤ 0 은 경고 없이 제외, 납기 NULL 은 제외 + 경고
/// ③ 납기 &lt; 기준일 → 첫날 / 휴무일 납기 → 직전 근무일(그 날이 기준일 이전이면 첫날) / Dates 마지막 날 뒤 → 무시
/// ④ knownItems(활성 MD_Item) 밖 품번 → 제외 + 경고
/// </summary>
public static class DemandRules
{
    public const string StatusConfirmed = "Confirmed";
    public const string StatusOpen      = "Open";

    public sealed record OrderRow(int SoId, string? SoNumber, int? SoLineNo, string? CustomerId, string ItemNo,
                                  DateOnly? RequestedDeliveryDate, decimal OrderQty, decimal ShippedQty, string? Status);

    public sealed record PlanRow(string? CustomerId, string ItemNo, DateOnly PlanDate, decimal ScheduledQty);

    /// <summary>Demand 키 = ItemNo(OrdinalIgnoreCase), 값 길이 = dates.Count. PlanDemand 는 일별 계획 몫만(표시용, 수주 몫 = Demand − PlanDemand).</summary>
    public sealed record Result(IReadOnlyDictionary<string, double[]> Demand, List<string> Warnings,
                                IReadOnlyDictionary<string, double[]>? PlanDemand = null);

    public static Result Build(IReadOnlyList<OrderRow> orders, DateOnly baseDate, IReadOnlyList<string> dates, IApsCalendar cal,
                               bool includeOpen, string? customerId, IReadOnlySet<string> knownItems)
        => Build(orders, Array.Empty<PlanRow>(), baseDate, dates, cal, includeOpen, customerId, knownItems);

    /// <summary>스펙 2026-09-29 §7 — 일별 계획: 고객 필터 동일, 0 이하 무시, 휴무일 → 직전 근무일, 기준일 전(지난 계획)은 버림, 미등록 품번 경고 1건/품번.</summary>
    public static Result Build(IReadOnlyList<OrderRow> orders, IReadOnlyList<PlanRow> plans, DateOnly baseDate, IReadOnlyList<string> dates, IApsCalendar cal,
                               bool includeOpen, string? customerId, IReadOnlySet<string> knownItems)
    {
        var demand   = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        if (dates.Count == 0) return new Result(demand, warnings, new Dictionary<string, double[]>());

        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < dates.Count; i++) index[dates[i]] = i;
        var last = ApsCalendar.Parse(dates[^1]);

        foreach (var o in orders)
        {
            if (!IsWanted(o.Status, includeOpen)) continue;
            if (customerId is not null && !string.Equals(o.CustomerId, customerId, StringComparison.OrdinalIgnoreCase)) continue;

            var remain = o.OrderQty - o.ShippedQty;
            if (remain <= 0) continue;

            if (o.RequestedDeliveryDate is not DateOnly due)
            {
                warnings.Add($"수주 {Label(o)} 품번 {o.ItemNo}: 납기가 없어 잔량 {N(remain)}개를 수요에서 제외했습니다.");
                continue;
            }
            if (!knownItems.Contains(o.ItemNo))
            {
                warnings.Add($"품번 {o.ItemNo} 은 MD_Item 에 없어 수주 {Label(o)} 잔량 {N(remain)}개를 제외했습니다.");
                continue;
            }

            int slot;
            if (due < baseDate) slot = 0;
            else
            {
                if (!cal.IsWorkday(due)) due = ApsCalendar.Parse(ApsCalendar.PrevWorkDate(cal, ApsCalendar.Iso(due)));
                if (due < baseDate) slot = 0;
                else if (due > last) continue;
                else if (!index.TryGetValue(ApsCalendar.Iso(due), out slot)) continue;
            }

            if (!demand.TryGetValue(o.ItemNo, out var arr)) demand[o.ItemNo] = arr = new double[dates.Count];
            arr[slot] += (double)remain;
        }

        var planDemand = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
        var unknownPlan = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in plans)
        {
            if (customerId is not null && !string.Equals(p.CustomerId, customerId, StringComparison.OrdinalIgnoreCase)) continue;
            if (p.ScheduledQty <= 0) continue;
            if (!knownItems.Contains(p.ItemNo)) { unknownPlan[p.ItemNo] = unknownPlan.GetValueOrDefault(p.ItemNo) + p.ScheduledQty; continue; }

            if (p.PlanDate < baseDate) continue;   // 원본 날짜가 이미 지난 계획은 PO 로 바뀌었거나 취소된 것 — 버린다

            int slot;
            var d = p.PlanDate;
            if (!cal.IsWorkday(d)) d = ApsCalendar.Parse(ApsCalendar.PrevWorkDate(cal, ApsCalendar.Iso(d)));
            if (d < baseDate) slot = 0;   // 휴무일 폴백이 기준일 이전으로 접히면 수주와 같이 첫날에 몬다
            else if (d > last) continue;
            else if (!index.TryGetValue(ApsCalendar.Iso(d), out slot)) continue;

            if (!demand.TryGetValue(p.ItemNo, out var arr)) demand[p.ItemNo] = arr = new double[dates.Count];
            if (!planDemand.TryGetValue(p.ItemNo, out var pl)) planDemand[p.ItemNo] = pl = new double[dates.Count];
            arr[slot] += (double)p.ScheduledQty;
            pl[slot]  += (double)p.ScheduledQty;
        }
        foreach (var (item, qty) in unknownPlan.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            warnings.Add($"품번 {item} 은 MD_Item 에 없어 일별 계획 {N(qty)}개를 제외했습니다.");

        return new Result(demand, warnings, planDemand);
    }

    static bool IsWanted(string? status, bool includeOpen) =>
        string.Equals(status, StatusConfirmed, StringComparison.OrdinalIgnoreCase)
        || (includeOpen && string.Equals(status, StatusOpen, StringComparison.OrdinalIgnoreCase));

    static string Label(OrderRow o)
    {
        var no = string.IsNullOrEmpty(o.SoNumber) ? $"#{o.SoId}" : o.SoNumber;
        return o.SoLineNo is int ln ? $"{no}-{ln}" : no;
    }

    static string N(decimal v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
