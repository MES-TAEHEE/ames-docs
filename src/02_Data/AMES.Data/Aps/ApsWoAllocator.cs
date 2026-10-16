namespace AMES.Data.Aps;

/// <summary>
/// 스펙 §8.1 수주 배정(FIFO). 「WO 생성」 1회 동안 인스턴스 하나가 품번별 수주 잔량을 들고 있어
/// 같은 품번의 다음 계획일 호출이 앞 계획일이 채운 뒤를 잇는다. 순수 클래스 — 잔량(기발행 WO 차감)은 리포지토리 SQL 이 계산해 넘긴다.
/// </summary>
public sealed class ApsWoAllocator
{
    /// <summary>RemainQty = OrderQty − ShippedQty − 기발행 WO 수량(취소 제외). Status='Confirmed' 행만 넘어온다.</summary>
    public sealed record OpenOrder(int SoId, string ItemNo, string? SoNumber, int? SoLineNo, DateOnly? DueDate, decimal RemainQty);

    /// <summary>SoId null = 재고 보충(수주 없음). DueDate 는 배정 수주의 납기(없으면 null).</summary>
    public sealed record Piece(int? SoId, decimal Qty, DateOnly? DueDate);

    sealed class Bucket(OpenOrder order)
    {
        public OpenOrder Order  { get; } = order;
        public decimal   Remain { get; set; } = order.RemainQty;
    }

    readonly Dictionary<string, List<Bucket>> _byItem = new(StringComparer.OrdinalIgnoreCase);

    public ApsWoAllocator(IEnumerable<OpenOrder> orders)
    {
        foreach (var g in orders.Where(o => o.RemainQty > 0).GroupBy(o => o.ItemNo, StringComparer.OrdinalIgnoreCase))
            _byItem[g.Key] = g
                .OrderBy(o => o.DueDate is null ? 1 : 0)
                .ThenBy(o => o.DueDate)
                .ThenBy(o => o.SoNumber, StringComparer.Ordinal)
                .ThenBy(o => o.SoLineNo)
                .Select(o => new Bucket(o))
                .ToList();
    }

    /// <summary>qty 를 납기순 잔량에 FIFO 로 채우고 남는 양은 SoId null 1건. qty ≤ 0 → 빈 목록. 호출마다 잔량이 줄어든다.</summary>
    public IReadOnlyList<Piece> Allocate(string itemNo, decimal qty)
    {
        var pieces = new List<Piece>();
        if (qty <= 0) return pieces;

        decimal left = qty;
        if (_byItem.TryGetValue(itemNo, out var buckets))
            foreach (var b in buckets)
            {
                if (left <= 0) break;
                if (b.Remain <= 0) continue;
                var take = Math.Min(b.Remain, left);
                pieces.Add(new Piece(b.Order.SoId, take, b.Order.DueDate));
                b.Remain -= take;
                left     -= take;
            }
        if (left > 0) pieces.Add(new Piece(null, left, null));
        return pieces;
    }
}
