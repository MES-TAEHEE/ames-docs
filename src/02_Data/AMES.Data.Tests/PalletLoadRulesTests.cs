using AMES.Data.Services;
using Xunit;

namespace AMES.Data.Tests;

/// <summary>
/// POP 팔레트 적재 규칙 정본 — 바코드 구분·적재 가능 판정·칩 문구.
/// 10-21 사용자 결정: 같은 금형·색상 형제 품번(LH/RH)은 한 팔레트에 허용하고 적입수량은 품번마다 따로 센다.
/// </summary>
public class PalletLoadRulesTests
{
    static readonly HashSet<string> NoSiblings = new(StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> LhRh = new(StringComparer.OrdinalIgnoreCase) { "ITEM-LH", "ITEM-RH" };

    static PalletLoadRules.PalletState Pallet(bool exists = true, bool active = true, bool shipped = false)
        => new(exists, active, shipped, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));

    static PalletLoadRules.PalletState Loaded(params (string Part, int Count)[] loaded)
        => new(true, true, false, loaded.ToDictionary(x => x.Part, x => x.Count, StringComparer.OrdinalIgnoreCase));

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
        => Assert.Equal(PalletLoadOutcome.NotFound, PalletLoadRules.Check(Pallet(exists: false), "ITEM-A", 24, NoSiblings));

    [Fact]
    public void Inactive_pallet_is_rejected()
        => Assert.Equal(PalletLoadOutcome.Inactive, PalletLoadRules.Check(Pallet(active: false), "ITEM-A", 24, NoSiblings));

    [Fact]
    public void Shipped_pallet_is_rejected_until_returned()
        => Assert.Equal(PalletLoadOutcome.Shipped, PalletLoadRules.Check(Pallet(shipped: true), "ITEM-A", 24, NoSiblings));

    [Fact]
    public void Pallet_holding_an_unrelated_item_is_rejected()
        => Assert.Equal(PalletLoadOutcome.Mixed, PalletLoadRules.Check(Loaded(("ITEM-B", 3)), "ITEM-A", 24, NoSiblings));

    [Fact]
    public void Sibling_from_the_same_mold_and_color_may_share_the_pallet()
        => Assert.Equal(PalletLoadOutcome.Ok, PalletLoadRules.Check(Loaded(("ITEM-LH", 3)), "ITEM-RH", 24, LhRh));

    [Fact]
    public void Pallet_with_a_sibling_and_an_unrelated_item_is_still_rejected()
        => Assert.Equal(PalletLoadOutcome.Mixed, PalletLoadRules.Check(Loaded(("ITEM-LH", 3), ("ITEM-X", 1)), "ITEM-RH", 24, LhRh));

    [Fact]
    public void Same_item_compares_case_insensitively()
        => Assert.Equal(PalletLoadOutcome.Ok, PalletLoadRules.Check(Loaded(("item-a", 3)), "ITEM-A", 24, NoSiblings));

    [Fact]
    public void Full_pallet_is_rejected()
        => Assert.Equal(PalletLoadOutcome.Full, PalletLoadRules.Check(Loaded(("ITEM-A", 24)), "ITEM-A", 24, NoSiblings));

    [Fact]
    public void Capacity_is_counted_per_item_so_a_full_sibling_does_not_block_the_other()
        => Assert.Equal(PalletLoadOutcome.Ok, PalletLoadRules.Check(Loaded(("ITEM-LH", 24), ("ITEM-RH", 23)), "ITEM-RH", 24, LhRh));

    [Fact]
    public void Full_item_is_rejected_even_when_its_sibling_has_room()
        => Assert.Equal(PalletLoadOutcome.Full, PalletLoadRules.Check(Loaded(("ITEM-LH", 24), ("ITEM-RH", 3)), "ITEM-LH", 24, LhRh));

    [Fact]
    public void One_below_capacity_still_loads()
        => Assert.Equal(PalletLoadOutcome.Ok, PalletLoadRules.Check(Loaded(("ITEM-A", 23)), "ITEM-A", 24, NoSiblings));

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public void Missing_pallet_qty_means_no_limit(int? palletQty)
        => Assert.Equal(PalletLoadOutcome.Ok, PalletLoadRules.Check(Loaded(("ITEM-A", 999)), "ITEM-A", palletQty, NoSiblings));

    [Fact]
    public void Empty_pallet_accepts_any_item()
        => Assert.Equal(PalletLoadOutcome.Ok, PalletLoadRules.Check(Pallet(), "ITEM-A", 24, NoSiblings));

    [Theory]
    [InlineData(3, 24, false)]
    [InlineData(24, 24, true)]
    [InlineData(5, null, false)]
    public void IsFull_follows_capacity(int loaded, int? palletQty, bool full)
        => Assert.Equal(full, PalletLoadRules.IsFull(loaded, palletQty));

    [Fact]
    public void Loaded_count_of_an_item_defaults_to_zero()
        => Assert.Equal(0, Loaded(("ITEM-LH", 3)).CountOf("ITEM-RH"));

    [Fact]
    public void Capacity_text_for_a_single_item_is_count_over_capacity()
        => Assert.Equal("3 / 24", PalletLoadRules.CapacityText(Loaded(("ITEM-A", 3)), "ITEM-A", 24));

    [Fact]
    public void Capacity_text_for_an_empty_pallet_is_zero()
        => Assert.Equal("0 / 24", PalletLoadRules.CapacityText(Pallet(), "ITEM-A", 24));

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void Capacity_text_shows_dash_when_unlimited(int? palletQty)
        => Assert.Equal("3 / —", PalletLoadRules.CapacityText(Loaded(("ITEM-A", 3)), "ITEM-A", palletQty));

    [Fact]
    public void Capacity_text_with_siblings_names_each_part_selected_item_first()
        => Assert.Equal("ITEM-RH 11 · ITEM-LH 12 / 24",
                        PalletLoadRules.CapacityText(Loaded(("ITEM-LH", 12), ("ITEM-RH", 11)), "ITEM-RH", 24));
}
