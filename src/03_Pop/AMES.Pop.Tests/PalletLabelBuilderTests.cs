using AMES.Devices;
using Xunit;

namespace AMES.Pop.Tests;

/// <summary>MD-034 팔레트 라벨 — 현장 라벨지 70×40mm(560×320 dot) 좌표, QR 내용 = 팔레트 번호.</summary>
public class PalletLabelBuilderTests
{
    static readonly PalletLabel Sample = new("EOS-PLT-2611-0001", "EXTERNAL", "CUS-SAV", new DateTime(2026, 11, 6));
    static readonly string NL = Environment.NewLine;

    [Fact]
    public void Label_size_is_the_site_label_stock()
    {
        var zpl = PalletLabelBuilder.Build(Sample);
        Assert.StartsWith("^XA", zpl);
        Assert.EndsWith("^XZ", zpl);
        Assert.Contains("^PW560", zpl);
        Assert.Contains("^LL320", zpl);
        Assert.DoesNotContain("^LH", zpl);
        Assert.Contains("^PQ1", zpl);
    }

    [Fact]
    public void Qr_and_text_carry_the_pallet_number()
    {
        var zpl = PalletLabelBuilder.Build(Sample);
        Assert.Contains($"^FO40,25{NL}^BQN,2,6,Q{NL}^FH^FDQA,EOS-PLT-2611-0001^FS", zpl);
        Assert.Contains("^FH^FDEXTERNAL · CUS-SAV^FS", zpl);
        Assert.Contains("^FH^FD11/06/2026^FS", zpl);
        Assert.Contains($"^FO40,228{NL}^A0N,56,46{NL}^FH^FDEOS-PLT-2611-0001^FS", zpl);
    }

    [Fact]
    public void Internal_pallet_shows_use_only_and_copies_are_clamped()
    {
        var zpl = PalletLabelBuilder.Build(Sample with { Use = "INTERNAL", Customer = null }, copies: 500);
        Assert.Contains("^FH^FDINTERNAL^FS", zpl);
        Assert.Contains("^PQ99", zpl);
        Assert.Contains("^PQ1", PalletLabelBuilder.Build(Sample, copies: 0));
    }

    [Fact]
    public void Zpl_control_characters_are_removed_and_underscore_escaped()
    {
        var zpl = PalletLabelBuilder.Build(Sample with { PalletNo = "EOS-PLT_2611^0001~" });
        Assert.Contains("^FDQA,EOS-PLT_5F26110001^FS", zpl);
    }

    [Theory]
    [InlineData("172.29.4.10", "Tcp", "172.29.4.10", 9100, "")]
    [InlineData("172.29.4.10:6101", "Tcp", "172.29.4.10", 6101, "")]
    [InlineData(" Tcp:zebra-01:9100 ", "Tcp", "zebra-01", 9100, "")]
    [InlineData("Spooler:ZDesigner ZD421-203dpi", "Spooler", "127.0.0.1", 9100, "ZDesigner ZD421-203dpi")]
    public void Printer_target_parses(string value, string mode, string host, int port, string name)
    {
        Assert.True(ZplPrinterOptions.TryParseTarget(value, out var o));
        Assert.Equal(mode, o.Mode);
        Assert.Equal(host, o.Host);
        Assert.Equal(port, o.Port);
        Assert.Equal(name, o.PrinterName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("Spooler:")]
    [InlineData("172.29.4.10:abc")]
    [InlineData("172.29.4.10:70000")]
    [InlineData("my printer")]
    [InlineData(":9100")]
    public void Bad_printer_target_is_rejected(string? value)
        => Assert.False(ZplPrinterOptions.TryParseTarget(value, out _));
}
