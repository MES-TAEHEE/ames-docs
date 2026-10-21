namespace AMES.Data.Services;

/// <summary>POP 팔레트 적재 판정 결과 — 확정 트랜잭션 안에서 팔레트 행을 잠그고 낸다.</summary>
public enum PalletLoadOutcome
{
    Ok,
    /// <summary>MD_Pallet 에 없는 번호.</summary>
    NotFound,
    /// <summary>사용 안 함(ActiveFlag 0) — 분실·폐기.</summary>
    Inactive,
    /// <summary>출고된 채 회수되지 않았다(ShippedFlag 1).</summary>
    Shipped,
    /// <summary>형제가 아닌 다른 품번이 실려 있다 — 혼적 금지.</summary>
    Mixed,
    /// <summary>그 품번이 적입수량(MD_Item.PalletQty)만큼 찼다 — 새 팔레트를 스캔해야 한다.</summary>
    Full,
}

/// <summary>
/// POP INJ/IMG 확정 때 OK 제품을 팔레트에 싣는 규칙 정본(2026-10-21 사용자 결정).
/// 팔레트 바코드는 <see cref="BarcodePrefix"/> 로 LOT 과 가르고, 한 팔레트에는 한 품번 — 단 **같은 금형·색상에서 같은 샷에 나오는
/// 형제 품번(LH/RH)은 함께 실을 수 있다**. 적입수량은 **품번마다 따로** 센다(APS 가 품번마다 자기 PalletQty 를 포장 단위로 쓰는 것과 같은 뜻 —
/// 계획 1포장 = 팔레트 1개). 적입수량이 없는 품번(NULL·0 이하)은 생산을 막지 않게 제한 없이 싣는다.
/// </summary>
public static class PalletLoadRules
{
    /// <summary>MD-034 가 발행하는 번호 양식 `EOS-PLT-yyMM-nnnn` 의 앞부분 — 스캔칸이 LOT 과 팔레트를 이것으로 가른다.</summary>
    public const string BarcodePrefix = "EOS-PLT-";

    /// <param name="Exists">MD_Pallet 행이 있다.</param>
    /// <param name="Loaded">지금 실려 있는(Qty &gt; 0) 제품 수 — 품번별(대소문자 무시).</param>
    public sealed record PalletState(bool Exists, bool Active, bool Shipped, IReadOnlyDictionary<string, int> Loaded)
    {
        public static readonly PalletState Missing =
            new(false, false, false, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));

        public int CountOf(string itemNo) => Loaded.TryGetValue(itemNo, out var n) ? n : 0;
        public int TotalCount => Loaded.Values.Sum();
    }

    public static bool IsPalletBarcode(string? raw)
        => NormalizePalletNo(raw) is { } p && p.StartsWith(BarcodePrefix, StringComparison.Ordinal);

    public static string? NormalizePalletNo(string? raw)
    {
        var s = raw?.Trim().ToUpperInvariant();
        return string.IsNullOrEmpty(s) ? null : s;
    }

    /// <param name="siblings">itemNo 와 같은 금형·색상에 묶인 품번들(itemNo 자신 포함 여부 무관) — 이들만 함께 실릴 수 있다.</param>
    public static PalletLoadOutcome Check(PalletState s, string itemNo, int? palletQty, IReadOnlySet<string> siblings)
    {
        if (!s.Exists) return PalletLoadOutcome.NotFound;
        if (!s.Active)  return PalletLoadOutcome.Inactive;
        if (s.Shipped)  return PalletLoadOutcome.Shipped;
        foreach (var (part, count) in s.Loaded)
        {
            if (count <= 0) continue;
            if (string.Equals(part, itemNo, StringComparison.OrdinalIgnoreCase)) continue;
            if (!siblings.Contains(part)) return PalletLoadOutcome.Mixed;
        }
        return IsFull(s.CountOf(itemNo), palletQty) ? PalletLoadOutcome.Full : PalletLoadOutcome.Ok;
    }

    public static bool IsFull(int loaded, int? palletQty) => palletQty is > 0 && loaded >= palletQty;

    /// <summary>
    /// 칩 문구 — 한 품번만 실렸으면 `3 / 24`, 형제가 같이 실렸으면 `품번 n · 품번 n / 24`(고른 품번 먼저, 나머지는 품번순).
    /// 적입수량은 고른 품번의 것이고 없으면 `—`.
    /// </summary>
    public static string CapacityText(PalletState s, string itemNo, int? palletQty)
    {
        var cap = palletQty is > 0 ? palletQty.Value.ToString() : "—";
        var others = s.Loaded.Where(kv => kv.Value > 0 && !string.Equals(kv.Key, itemNo, StringComparison.OrdinalIgnoreCase))
                             .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).ToList();
        if (others.Count == 0) return $"{s.CountOf(itemNo)} / {cap}";
        var parts = new List<string> { $"{itemNo} {s.CountOf(itemNo)}" };
        parts.AddRange(others.Select(kv => $"{kv.Key} {kv.Value}"));
        return $"{string.Join(" · ", parts)} / {cap}";
    }
}
