using AMES.Contracts.Enums;
using AMES.Data.Repositories;
using AMES.Data.Services;
using AMES.Pop.Common;
using Xunit;

namespace AMES.Pop.Tests;

/// <summary>INJ/IMG-MAIN 팔레트 칩 문구·색과 확정 결과 → 안내 키 매핑.</summary>
public class PalletChipTests
{
    static PalletStockRepository.PalletStatus Status(int loaded, int? palletQty, (string Part, int Count)? sibling = null)
    {
        var parts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (loaded > 0) parts["ITEM-A"] = loaded;
        if (sibling is { } s) parts[s.Part] = s.Count;
        return new("EOS-PLT-2610-0002", new PalletLoadRules.PalletState(true, true, false, parts), "ITEM-A", palletQty,
                   PalletLoadRules.IsFull(loaded, palletQty) ? PalletLoadOutcome.Full : PalletLoadOutcome.Ok);
    }

    [Fact]
    public void No_pallet_shows_a_red_dash()
        => Assert.Equal(("—", "bad"), PalletChip.Of(null));

    [Fact]
    public void Pallet_with_room_shows_number_and_count_in_green()
        => Assert.Equal(("EOS-PLT-2610-0002 · 3 / 24", "ok"), PalletChip.Of(Status(3, 24)));

    [Fact]
    public void Full_pallet_turns_red()
        => Assert.Equal(("EOS-PLT-2610-0002 · 24 / 24", "bad"), PalletChip.Of(Status(24, 24)));

    [Fact]
    public void Unlimited_pallet_shows_a_dash_for_capacity()
        => Assert.Equal(("EOS-PLT-2610-0002 · 5 / —", "ok"), PalletChip.Of(Status(5, null)));

    [Fact]
    public void Sibling_counts_are_listed_per_part_with_the_selected_part_first()
        => Assert.Equal(("EOS-PLT-2610-0002 · ITEM-A 12 · ITEM-B 11 / 24", "ok"), PalletChip.Of(Status(12, 24, ("ITEM-B", 11))));

    [Fact]
    public void Full_selected_part_turns_red_even_if_the_sibling_has_room()
        => Assert.Equal("bad", PalletChip.Of(Status(24, 24, ("ITEM-B", 3))).Css);

    [Theory]
    [InlineData(InjConfirmOutcome.PalletNotFound,    "PalletNotFound")]
    [InlineData(InjConfirmOutcome.PalletUnavailable, "PalletUnavailable")]
    [InlineData(InjConfirmOutcome.PalletMixed,       "PalletMixed")]
    [InlineData(InjConfirmOutcome.PalletFull,        "PalletFull")]
    [InlineData(InjConfirmOutcome.Confirmed,         null)]
    [InlineData(InjConfirmOutcome.NotFound,          null)]
    public void Inj_pallet_outcomes_map_to_notice_keys(InjConfirmOutcome o, string? key)
        => Assert.Equal(key, PalletChip.NoticeKey(o));

    [Theory]
    [InlineData(ImgConfirmOutcome.PalletNotFound,    "PalletNotFound")]
    [InlineData(ImgConfirmOutcome.PalletUnavailable, "PalletUnavailable")]
    [InlineData(ImgConfirmOutcome.PalletMixed,       "PalletMixed")]
    [InlineData(ImgConfirmOutcome.PalletFull,        "PalletFull")]
    [InlineData(ImgConfirmOutcome.Confirmed,         null)]
    public void Img_pallet_outcomes_map_to_notice_keys(ImgConfirmOutcome o, string? key)
        => Assert.Equal(key, PalletChip.NoticeKey(o));

    [Theory]
    [InlineData(PalletLoadOutcome.NotFound, "PalletNotFound")]
    [InlineData(PalletLoadOutcome.Inactive, "PalletUnavailable")]
    [InlineData(PalletLoadOutcome.Shipped,  "PalletUnavailable")]
    [InlineData(PalletLoadOutcome.Mixed,    "PalletMixed")]
    [InlineData(PalletLoadOutcome.Full,     "PalletFull")]
    [InlineData(PalletLoadOutcome.Ok,       null)]
    public void Pallet_scan_outcomes_map_to_notice_keys(PalletLoadOutcome o, string? key)
        => Assert.Equal(key, PalletChip.NoticeKey(o));

    [Theory]
    [InlineData(PalletLoadOutcome.Ok,       true)]
    [InlineData(PalletLoadOutcome.Mixed,    true)]
    [InlineData(PalletLoadOutcome.Full,     true)]
    [InlineData(PalletLoadOutcome.NotFound, false)]
    [InlineData(PalletLoadOutcome.Inactive, false)]
    [InlineData(PalletLoadOutcome.Shipped,  false)]
    public void Only_a_usable_pallet_becomes_the_current_pallet(PalletLoadOutcome o, bool accepted)
        => Assert.Equal(accepted, PalletChip.Accepts(o));
}
