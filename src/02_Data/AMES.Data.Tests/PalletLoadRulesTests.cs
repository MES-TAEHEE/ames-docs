using AMES.Data.Services;
using Xunit;

namespace AMES.Data.Tests;

/// <summary>POP 팔레트 적재 규칙 정본 — 바코드 구분·적재 가능 판정·칩 문구.</summary>
public class PalletLoadRulesTests
{
    static PalletLoadRules.PalletState Pallet(int loaded = 0, string? part = null, bool exists = true, bool active = true, bool shipped = false)
        => new(exists, active, shipped, part, loaded);

    [Theory]
    [InlineData("EOS-PLT-2610-0002", true)]
    [InlineData("  eos-plt-2610-0002 ", true)]
    [InlineData("A91I10001", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Pallet_barcode_is_told_apart_from_a_lot_by_its_prefix(string? raw, bool expected)
        => Assert.Equal(expected, PalletLoadRules.IsPalletBarcode(raw));

    [Fact]
    public void Pallet_no_is_trimmed_and_upper_cased()
        => Assert.Equal("EOS-PLT-2610-0002", PalletLoadRules.NormalizePalletNo("  eos-plt-2610-0002 "));

    [Fact]
    public void Unknown_pallet_is_rejected()
        => Assert.Equal(PalletLoadOutcome.NotFound, PalletLoadRules.Check(Pallet(exists: false), "ITEM-A", 24));

    [Fact]
    public void Inactive_pallet_is_rejected()
        => Assert.Equal(PalletLoadOutcome.Inactive, PalletLoadRules.Check(Pallet(active: false), "ITEM-A", 24));

    [Fact]
    public void Shipped_pallet_is_rejected_until_returned()
        => Assert.Equal(PalletLoadOutcome.Shipped, PalletLoadRules.Check(Pallet(shipped: true), "ITEM-A", 24));

    [Fact]
    public void Pallet_holding_another_item_is_rejected()
        => Assert.Equal(PalletLoadOutcome.Mixed, PalletLoadRules.Check(Pallet(loaded: 3, part: "ITEM-B"), "ITEM-A", 24));

    [Fact]
    public void Same_item_compares_case_insensitively()
        => Assert.Equal(PalletLoadOutcome.Ok, PalletLoadRules.Check(Pallet(loaded: 3, part: "item-a"), "ITEM-A", 24));

    [Fact]
    public void Full_pallet_is_rejected()
        => Assert.Equal(PalletLoadOutcome.Full, PalletLoadRules.Check(Pallet(loaded: 24, part: "ITEM-A"), "ITEM-A", 24));

    [Fact]
    public void One_below_capacity_still_loads()
        => Assert.Equal(PalletLoadOutcome.Ok, PalletLoadRules.Check(Pallet(loaded: 23, part: "ITEM-A"), "ITEM-A", 24));

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public void Missing_pallet_qty_means_no_limit(int? palletQty)
        => Assert.Equal(PalletLoadOutcome.Ok, PalletLoadRules.Check(Pallet(loaded: 999, part: "ITEM-A"), "ITEM-A", palletQty));

    [Fact]
    public void Empty_pallet_accepts_any_item()
        => Assert.Equal(PalletLoadOutcome.Ok, PalletLoadRules.Check(Pallet(), "ITEM-A", 24));

    [Fact]
    public void Pallet_emptied_by_shipment_and_returned_is_reusable_for_another_item()
        => Assert.Equal(PalletLoadOutcome.Ok, PalletLoadRules.Check(Pallet(loaded: 0, part: null), "ITEM-C", 8));

    [Theory]
    [InlineData(3, 24, false)]
    [InlineData(24, 24, true)]
    [InlineData(5, null, false)]
    public void IsFull_follows_capacity(int loaded, int? palletQty, bool full)
        => Assert.Equal(full, PalletLoadRules.IsFull(loaded, palletQty));

    [Theory]
    [InlineData(3, 24, "3 / 24")]
    [InlineData(3, null, "3 / —")]
    [InlineData(3, 0, "3 / —")]
    public void Capacity_text_shows_dash_when_unlimited(int loaded, int? palletQty, string text)
        => Assert.Equal(text, PalletLoadRules.CapacityText(loaded, palletQty));
}
