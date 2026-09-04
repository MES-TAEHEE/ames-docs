using AMES.Devices;
using Xunit;

namespace AMES.Pop.Tests;

public class ImgScanParserTests
{
    const char RS = '\u001E', GS = '\u001D', EOT = '\u0004';

    static string Full(string lot = "A94W10002") =>
        $"[)>{RS}06{GS}VBYU5{GS}P83345P8000RBQ{GS}S81328046{GS}T2609041P8A{lot}{GS}E{GS}C:{RS}{EOT}";

    [Fact]
    public void Serial_scan_with_control_chars_yields_lot_from_T_token()
        => Assert.Equal("A94W10002", ImgScanParser.ExtractLotCode(Full()));

    [Fact]
    public void Hid_wedge_that_dropped_control_chars_still_yields_lot()
    {
        var raw = Full().Replace(RS.ToString(), "").Replace(GS.ToString(), "").Replace(EOT.ToString(), "");
        Assert.Equal("A94W10002", ImgScanParser.ExtractLotCode(raw));
    }

    [Fact]
    public void Aim_symbology_prefix_is_ignored()
        => Assert.Equal("A94W10002", ImgScanParser.ExtractLotCode("]d2" + Full()));

    [Fact]
    public void Item_no_containing_T_and_digits_does_not_confuse_parser()
    {
        var raw = Full("B01W10777").Replace("P83345P8000RBQ", "PT123456ABC");
        Assert.Equal("B01W10777", ImgScanParser.ExtractLotCode(raw));
    }

    [Fact]
    public void Plain_lot_code_passes_through_trimmed()
        => Assert.Equal("A94W10002", ImgScanParser.ExtractLotCode("  A94W10002 \r"));

    [Fact]
    public void Unrecognised_datamatrix_returns_null()
        => Assert.Null(ImgScanParser.ExtractLotCode($"[)>{RS}06{GS}VBYU5{GS}E{RS}{EOT}"));

    [Fact]
    public void Empty_returns_null()
        => Assert.Null(ImgScanParser.ExtractLotCode("   "));
}
