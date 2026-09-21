using System.Text;
using AMES.Pda.Services;
using Xunit;

namespace AMES.InjAgent.Tests;

public class SparePartLabelTests
{
    [Fact]
    public void Printer_selection_prefers_ZT411_and_keeps_an_explicit_choice()
    {
        var printers = new[]
        {
            new SparePartPrinter.Device("AA", "Other Printer"),
            new SparePartPrinter.Device("BB", "ZDesigner ZT411-203dpi ZPL")
        };

        Assert.Equal("BB", SparePartPrinter.SelectAddress(printers, null));
        Assert.Equal("AA", SparePartPrinter.SelectAddress(printers, "AA"));
        Assert.Equal("CC", SparePartPrinter.SelectAddress(printers, "CC"));
    }

    [Theory]
    [InlineData("AA:BB:CC:DD:EE:FF", true)]
    [InlineData("aa:bb:cc:dd:ee:ff", true)]
    [InlineData("ZT411", false)]
    [InlineData("AA:BB:CC:DD:EE", false)]
    public void Manual_printer_address_is_validated(string address, bool expected) =>
        Assert.Equal(expected, SparePartPrinter.IsValidAddress(address));

    [Fact]
    public void Serialized_label_contains_serial_master_and_optional_extra_location()
    {
        var zpl = SparePartLabel.BuildSerialized("SPI-000000000001", "EOS-SP-A1-260001",
            "SERVO MOTOR", "DEMO VENDOR", "SP_EXTRA", "Tool crib shelf 4");

        Assert.Contains("^BQN", zpl);
        Assert.Contains("^LL400", zpl);
        Assert.Equal(5, SparePartLabel.PreviewVerticalOffsetPercent);
        Assert.Contains("^FO24,54^BQN,2,8", zpl);
        Assert.Contains("^FO244,56^A0N,48,40^FB536", zpl);
        Assert.Contains("^FO244,111^A0N,48,40^FB536", zpl);
        Assert.Contains("^FO244,166^A0N,44,36^FB536", zpl);
        Assert.Contains("^FO244,217^A0N,38,30^FB536", zpl);
        Assert.Contains("^FO28,268^A0N,56,48", zpl);
        Assert.Contains("^FO28,336^A0N,52,44", zpl);
        Assert.True(336 + 52 < SparePartLabel.LabelHeightDots);
        Assert.Contains("_53_50_49_2D_30_30_30_30_30_30_30_30_30_30_30_31", zpl);
        Assert.Contains("_45_4F_53_2D_53_50_2D_41_31_2D_32_36_30_30_30_31", zpl);
        Assert.Contains("_54_6F_6F_6C_20_63_72_69_62_20_73_68_65_6C_66_20_34", zpl);
        Assert.Contains("_28_54_6F_6F_6C", zpl);
    }

    [Fact]
    public void Pending_serials_continue_after_the_highest_visible_suffix()
    {
        var serials = SparePartLabel.NextSerials("EOS-SP-C9-260001",
            ["SPI-C9-260001-0001", "SPI-C9-260002-0099"], 3);

        Assert.Equal(["SPI-C9-260001-0002", "SPI-C9-260001-0003", "SPI-C9-260001-0004"], serials);
    }

    [Theory]
    [InlineData(2, 1, 1, false)]
    [InlineData(1, 1, 1, true)]
    [InlineData(10, 1, null, false)]
    [InlineData(1, 1, 0, false)]
    public void Release_warns_only_when_remaining_stock_is_below_safety_stock(
        decimal currentQty, decimal releaseQty, int? safetyStock, bool expected)
        => Assert.Equal(expected, SparePartValidation.FallsBelowSafetyStock(currentQty, releaseQty, safetyStock));

    [Fact]
    public void Missing_information_lists_only_empty_or_placeholder_fields()
    {
        Assert.Equal(new[] { "Spare Part No", "Part No", "Maker", "Location No" },
            SparePartValidation.MissingFields(("Spare Part No", null), ("Part No", ""),
                ("Maker", "  "), ("Location No", " - ")));
        Assert.Empty(SparePartValidation.MissingFields(("Spare Part No", "EOS-SP-C9-260001"),
            ("Part No", "SP-HTR-2KW"), ("Maker", "DEMO CONTROLS"), ("Location No", "SP-C1-04")));
        Assert.Equal(new[] { "Vendor", "Unit" }, SparePartValidation.MissingFields(
            ("Qty", "0"), ("Vendor", ""), ("Unit", null), ("Storage Location", "SP-C1-04")));
    }
    [Theory]
    [InlineData(203, 799, 400)]
    [InlineData(300, 1181, 591)]
    public async Task Label_keeps_100x50mm_one_copy_and_preserves_safe_scan_data(int dpi, int labelWidth, int labelHeight)
    {
        int D(int dots) => (int)Math.Round(dots * dpi / 203d);
        const string barcode = "EOS-SP-K9-269999";
        var zpl = SparePartLabel.Build(barcode, "PN_01", "Maker^XZ~JA", "MNT-A1-1", dpi);
        var encoded = string.Concat(Encoding.UTF8.GetBytes(barcode).Select(b => $"_{b:X2}"));
        Assert.Contains($"^BQN,2,{D(10)}^FH^FDLA,{encoded}^FS", zpl);
        Assert.Contains($"^PW{labelWidth}\n^LL{labelHeight}", zpl);
        Assert.Contains("^PQ1\n^XZ", zpl);
        Assert.DoesNotContain("Maker^XZ~JA", zpl);
        Assert.Contains("_5E_58_5A_7E_4A_41", zpl);
        foreach (var field in new[] { "Spare Part No", "Part No", "Maker", "Location No" })
            Assert.DoesNotContain($"^FD{field}^FS", zpl);
        Assert.Equal(5, zpl.Split("^FD").Length - 1); // QR plus four values, without captions.
        Assert.Contains($"^PW{labelWidth}\n^LL{labelHeight}", SparePartLabel.Build(barcode, null, null, null, dpi));
        Assert.Contains($"^FO{D(320)},{D(50)}^A0N,{D(44)},{D(26)}^FB{D(456)},1", zpl);
        var longValues = SparePartLabel.Build(barcode, new string('P', 40), new string('M', 40), new string('L', 40), dpi);
        Assert.Contains($"^FO{D(320)},{D(338)}^A0N,{D(26)},{D(22)}^FB{D(456)},2", longValues);
        Assert.True(D(338) + D(26) * 2 + D(2) <= labelHeight);
        Assert.True(D(320) + D(456) < labelWidth);
        foreach (var (length, modules, scale) in new[] { (17, 21, 10), (32, 25, 9), (53, 29, 8), (80, 37, 6) })
        {
            Assert.Contains($"^BQN,2,{D(scale)}", SparePartLabel.Build(new string('a', length), null, null, null, dpi));
            Assert.True(D(60) >= 4 * D(scale));
            Assert.True((modules + 8) * D(scale) <= D(320));
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => SparePartLabel.Build(barcode, null, null, null, 600));
        Assert.Throws<ArgumentException>(() => SparePartLabel.Build("\nBAD", null, null, null));
        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => new SparePartPrinter().DiscoverPrintersAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SparePartPrinter().SendAsync("", zpl));
    }

    [Fact]
    public void Late_bluetooth_names_replace_missing_names_without_losing_known_names()
    {
        const string address = "D8:71:4D:21:31:66";
        var device = new SparePartPrinter.Device(address, null);
        Assert.False(device.HasName);
        Assert.Equal("Unnamed device", device.DisplayName);
        device = device.WithName(" ZT41143-T410000Z ");
        Assert.True(device.HasName);
        Assert.Equal("ZT41143-T410000Z", device.DisplayName);
        Assert.Equal(device, device.WithName(null).WithName(" ").WithName(address));
        Assert.False(new SparePartPrinter.Device(address, address).HasName);
    }
}
