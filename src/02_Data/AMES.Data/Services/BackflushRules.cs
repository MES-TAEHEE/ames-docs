namespace AMES.Data.Services;

/// <param name="InjFlag">MD_Item.InjFlag — 사출품 자식. PNT 확정만 이 자식을 뺀다(includeInjection).</param>
public sealed record BackflushBomRow(string CompItemNo, string? ItemType, decimal QtyPer, decimal ScrapPct, string? Uom, bool Active, bool InjFlag = false);
public sealed record BackflushRequirement(string PartNo, decimal Qty, string? Uom);
public sealed record BackflushLot(string LotNo, decimal Qty, string? LocationNo = null);
/// <summary>양수 LOT 에서 자리표시 LOT 으로 옮기는 양(자리표시 음수 메우기).</summary>
public sealed record BackflushNet(string LotNo, decimal Qty);
/// <summary>양수 LOT 에서 빼는 양.</summary>
public sealed record BackflushTake(string LotNo, decimal Qty);
public sealed record BackflushAllocation(IReadOnlyList<BackflushNet> Nets, IReadOnlyList<BackflushTake> Takes,
                                         decimal PlaceholderTake, decimal Shortage);

/// <summary>
/// BOM 자동 차감 규칙(순수). 소요 = 유효 BOM 의 활성 MATERIAL 자식 × (1 + 스크랩%) × 1 EA,
/// 배분 = 라인 재고 FIFO → 모자라면 자리표시 LOT(음수 허용). 사용자 결정 2026-10-15.
/// PNT 는 사출품 자식(InjFlag 1)도 뺀다 — 코어 스캔이 없어 IMG 의 ConsumeCore 를 대신한다(2026-10-22 사용자 결정, <see cref="IncludesInjection"/>).
/// </summary>
public static class BackflushRules
{
    public const string MaterialType = "MATERIAL";

    public const string InjectionConsumingProcess = "PNT";

    public static string PlaceholderLotNo(string linePrefix, string partNo) => $"BF-{linePrefix}-{partNo}";

    /// <summary>그 공정의 확정·재작업 수리가 BOM 사출품 자식까지 빼는지 — PNT 만.</summary>
    public static bool IncludesInjection(string? processCode)
        => string.Equals(processCode, InjectionConsumingProcess, StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<BackflushRequirement> Requirements(IEnumerable<BackflushBomRow> rows, bool includeInjection = false)
    {
        var sum   = new Dictionary<string, (decimal Qty, string? Uom)>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        foreach (var r in rows)
        {
            if (!r.Active || r.QtyPer <= 0m) continue;
            var material  = string.Equals(r.ItemType, MaterialType, StringComparison.OrdinalIgnoreCase);
            if (!material && !(includeInjection && r.InjFlag)) continue;
            var qty = r.QtyPer * (1m + r.ScrapPct / 100m);
            if (sum.TryGetValue(r.CompItemNo, out var cur)) sum[r.CompItemNo] = (cur.Qty + qty, cur.Uom ?? r.Uom);
            else { sum[r.CompItemNo] = (qty, r.Uom); order.Add(r.CompItemNo); }
        }
        // 반올림은 합산 뒤 한 번 — 행마다 반올림하면 소수 4자리 QtyPer 두 행이 0 으로 사라진다.
        // 품번순 고정 — 같은 라인의 두 확정이 같은 자재를 다른 순서로 잠그면 교착한다
        return order.OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .Select(p => new BackflushRequirement(p, Round3(sum[p].Qty), sum[p].Uom)).ToList();
    }

    public static BackflushAllocation Allocate(decimal required, IReadOnlyList<BackflushLot> fifo, decimal placeholderQty)
    {
        var nets  = new List<BackflushNet>();
        var takes = new List<BackflushTake>();
        if (required <= 0m) return new(nets, takes, 0m, 0m);

        var remain = fifo.Select(l => l.Qty).ToArray();

        // 자리표시 음수부터 메운다 — 실물 LOT 이 올라온 뒤에도 자리표시가 영원히 음수로 남지 않게
        var placeholder = placeholderQty;
        for (var i = 0; i < fifo.Count && placeholder < 0m; i++)
        {
            var move = Math.Min(-placeholder, remain[i]);
            if (move <= 0m) continue;
            nets.Add(new(fifo[i].LotNo, move));
            remain[i] -= move; placeholder += move;
        }

        var need = required;
        for (var i = 0; i < fifo.Count && need > 0m; i++)
        {
            var take = Math.Min(need, remain[i]);
            if (take <= 0m) continue;
            takes.Add(new(fifo[i].LotNo, take));
            need -= take;
        }

        // 양수 자리표시(누군가 조정한 값)는 마지막 원천으로 쓰고, 그 뒤 남는 양만 부족이다
        var shortage = Math.Max(0m, need - Math.Max(0m, placeholder));
        return new(nets, takes, need, shortage);
    }

    static decimal Round3(decimal v) => Math.Round(v, 3, MidpointRounding.AwayFromZero);
}
