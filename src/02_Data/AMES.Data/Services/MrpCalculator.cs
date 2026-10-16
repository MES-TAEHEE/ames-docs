namespace AMES.Data.Services;

/// <summary>
/// PP-005 MRP 계산 규칙(순수 함수). DB 는 <c>PpRepository.RunMrp</c> 가 읽고 쓰며 여기서는 수요·BOM·공급만 받는다.
/// 소요 = Σ(WO 잔량 × QtyPer × (1 + ScrapPct/100)) 를 BOM leaf 까지 재귀 누적,
/// 부족 = 소요 − 재고 − 발주중(양수가 부족), 발주 기한 = 영향 WO 최단 납기 − 리드타임(부족일 때만).
/// </summary>
public static class MrpCalculator
{
    /// <summary>열린 WO 한 건의 잔여 수요.</summary>
    public sealed record Demand(int WoId, string ItemNo, decimal Qty, DateTime? DueDate);

    /// <summary>활성 BOM 행. ScrapPct 는 % 단위.</summary>
    public sealed record BomLine(string ParentItemNo, string CompItemNo, decimal QtyPer, decimal ScrapPct);

    /// <summary>자재의 가용 재고·발주중·리드타임(품목 마스터).</summary>
    public sealed record Supply(string ItemNo, decimal Stock, decimal OnOrder, int? LeadTimeDays);

    public sealed record WoDemand(int WoId, decimal Qty, DateTime? DueDate);

    public sealed record MaterialResult(string ItemNo, decimal Required, decimal Stock, decimal OnOrder,
        decimal Shortage, int? LeadTimeDays, DateTime? OrderDue, IReadOnlyList<WoDemand> Wos)
    {
        public bool IsShort => Shortage > 0;
    }

    public sealed record Result(List<MaterialResult> Materials, int WosConsidered, List<int> SkippedNoBom);

    public sealed class MrpCycleException(string path) : Exception($"BOM circular reference: {path}");

    public static Result Explode(IEnumerable<Demand> demands, IEnumerable<BomLine> bom,
        IReadOnlyDictionary<string, Supply> supply, DateTime today, int maxDepth = 20)
    {
        var children = bom.GroupBy(b => b.ParentItemNo, StringComparer.OrdinalIgnoreCase)
                          .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var acc = new Dictionary<string, List<WoDemand>>(StringComparer.OrdinalIgnoreCase);
        var considered = 0;
        var skipped = new List<int>();

        foreach (var d in demands)
        {
            if (d.Qty <= 0) continue;
            if (!children.ContainsKey(d.ItemNo)) { skipped.Add(d.WoId); continue; }
            considered++;
            Walk(d.ItemNo, d.Qty, d, acc, children, new Stack<string>(), maxDepth);
        }

        var materials = acc.Select(kv =>
        {
            var wos = kv.Value.OrderBy(w => w.DueDate ?? DateTime.MaxValue).ThenBy(w => w.WoId).ToList();
            var required = wos.Sum(w => w.Qty);
            supply.TryGetValue(kv.Key, out var s);
            var stock = s?.Stock ?? 0m;
            var onOrder = s?.OnOrder ?? 0m;
            var shortage = required - stock - onOrder;
            var earliestDue = wos.Select(w => w.DueDate).FirstOrDefault(x => x is not null);
            DateTime? orderDue = shortage > 0 && s?.LeadTimeDays is { } lt && earliestDue is { } due
                ? due.Date.AddDays(-lt) : null;
            return new MaterialResult(kv.Key, required, stock, onOrder, shortage, s?.LeadTimeDays, orderDue, wos);
        })
        .OrderByDescending(m => m.IsShort)
        .ThenBy(m => m.ItemNo, StringComparer.OrdinalIgnoreCase)
        .ToList();

        return new Result(materials, considered, skipped);
    }

    /// <summary>부족 자재(PR 미생성)의 영향을 받는 WO 집합.</summary>
    public static IEnumerable<int> BlockedWoIds(IEnumerable<MaterialResult> materials)
        => materials.Where(m => m.IsShort).SelectMany(m => m.Wos).Select(w => w.WoId).Distinct();

    static void Walk(string item, decimal qty, Demand origin, Dictionary<string, List<WoDemand>> acc,
        Dictionary<string, List<BomLine>> children, Stack<string> path, int maxDepth)
    {
        if (path.Contains(item, StringComparer.OrdinalIgnoreCase) || path.Count >= maxDepth)
            throw new MrpCycleException(string.Join(" → ", path.Reverse().Append(item)));

        if (!children.TryGetValue(item, out var lines))
        {
            if (!acc.TryGetValue(item, out var list)) acc[item] = list = new();
            var existing = list.FindIndex(w => w.WoId == origin.WoId);
            if (existing >= 0) list[existing] = list[existing] with { Qty = list[existing].Qty + qty };
            else list.Add(new WoDemand(origin.WoId, qty, origin.DueDate));
            return;
        }

        path.Push(item);
        foreach (var l in lines)
            Walk(l.CompItemNo, qty * l.QtyPer * (1 + l.ScrapPct / 100m), origin, acc, children, path, maxDepth);
        path.Pop();
    }
}
