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

    /// <summary>Demand 키 = ItemNo(OrdinalIgnoreCase), 값 길이 = dates.Count (double — 엔진 경계). 수요 0 품번은 키가 없다.</summary>
    public sealed record Result(IReadOnlyDictionary<string, double[]> Demand, List<string> Warnings);

    public static Result Build(IReadOnlyList<OrderRow> orders, DateOnly baseDate, IReadOnlyList<string> dates, IApsCalendar cal,
                               bool includeOpen, string? customerId, IReadOnlySet<string> knownItems)
    {
        var demand   = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        if (dates.Count == 0) return new Result(demand, warnings);

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
        return new Result(demand, warnings);
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
