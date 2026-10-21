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
    /// <summary>다른 품번이 실려 있다 — 혼적 금지.</summary>
    Mixed,
    /// <summary>적입수량(MD_Item.PalletQty)만큼 찼다 — 새 팔레트를 스캔해야 한다.</summary>
    Full,
}

/// <summary>
/// POP INJ/IMG 확정 때 OK 제품을 팔레트에 싣는 규칙 정본(2026-10-21 사용자 결정).
/// 팔레트 바코드는 <see cref="BarcodePrefix"/> 로 LOT 과 가르고, 한 팔레트에는 한 품번만, 품번의 적입수량까지만 싣는다.
/// 적입수량이 없는 품번(NULL·0 이하)은 생산을 막지 않게 제한 없이 싣는다.
/// </summary>
public static class PalletLoadRules
{
    /// <summary>MD-034 가 발행하는 번호 양식 `EOS-PLT-yyMM-nnnn` 의 앞부분 — 스캔칸이 LOT 과 팔레트를 이것으로 가른다.</summary>
    public const string BarcodePrefix = "EOS-PLT-";

    /// <param name="Exists">MD_Pallet 행이 있다.</param>
    /// <param name="LoadedPartNo">지금 실려 있는(Qty &gt; 0) 제품의 품번 — 비어 있으면 null.</param>
    /// <param name="LoadedCount">지금 실려 있는 제품 수(Qty &gt; 0 인 자식 행 수).</param>
    public sealed record PalletState(bool Exists, bool Active, bool Shipped, string? LoadedPartNo, int LoadedCount)
    {
        public static readonly PalletState Missing = new(false, false, false, null, 0);
    }

    public static bool IsPalletBarcode(string? raw)
        => NormalizePalletNo(raw) is { } p && p.StartsWith(BarcodePrefix, StringComparison.Ordinal);

    public static string? NormalizePalletNo(string? raw)
    {
        var s = raw?.Trim().ToUpperInvariant();
        return string.IsNullOrEmpty(s) ? null : s;
    }

    public static PalletLoadOutcome Check(PalletState s, string itemNo, int? palletQty)
    {
        if (!s.Exists) return PalletLoadOutcome.NotFound;
        if (!s.Active)  return PalletLoadOutcome.Inactive;
        if (s.Shipped)  return PalletLoadOutcome.Shipped;
        if (s.LoadedCount > 0 && s.LoadedPartNo is not null
            && !string.Equals(s.LoadedPartNo, itemNo, StringComparison.OrdinalIgnoreCase))
            return PalletLoadOutcome.Mixed;
        return IsFull(s.LoadedCount, palletQty) ? PalletLoadOutcome.Full : PalletLoadOutcome.Ok;
    }

    public static bool IsFull(int loaded, int? palletQty) => palletQty is > 0 && loaded >= palletQty;

    /// <summary>칩 문구 `3 / 24` — 적입수량이 없으면 `3 / —`.</summary>
    public static string CapacityText(int loaded, int? palletQty)
        => $"{loaded} / {(palletQty is > 0 ? palletQty.Value.ToString() : "—")}";
}
