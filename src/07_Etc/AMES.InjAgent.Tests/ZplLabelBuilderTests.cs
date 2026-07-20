using AMES.Devices;
using Xunit;

namespace AMES.InjAgent.Tests;

public class ZplLabelBuilderTests
{
    static ZplLabel Sample() => new(
        LotCode:    "L260717093000123-LINE-INJ-01-LH",
        ItemNo:     "83335-P8000RBQ",
        ItemName:   "GARNISH-RR DR UPR, LH",
        ColorCode:  "CBK",
        CavityPos:  "LH",
        LineId:     "LINE-INJ-01",
        ProducedAt: new DateTime(2026, 7, 17, 9, 30, 0));

    [Fact]
    public void Build_wraps_in_zpl_envelope()
    {
        var zpl = ZplLabelBuilder.Build(Sample());
        Assert.StartsWith("^XA", zpl);
        Assert.EndsWith("^XZ", zpl.TrimEnd());
    }

    [Fact]
    public void Build_contains_datamatrix_with_lotcode()
    {
        var zpl = ZplLabelBuilder.Build(Sample());
        Assert.Contains("^BXN", zpl);                               // DataMatrix
        Assert.Contains("^FDL260717093000123-LINE-INJ-01-LH^FS", zpl); // 바코드 내용 = LotCode
    }

    [Fact]
    public void Build_contains_item_color_cavity_and_date()
    {
        var zpl = ZplLabelBuilder.Build(Sample());
        Assert.Contains("83335-P8000RBQ", zpl);
        Assert.Contains("CBK", zpl);
        Assert.Contains("LH", zpl);
        Assert.Contains("2026-07-17 09:30", zpl);
    }

    [Fact]
    public void Build_strips_zpl_control_chars_from_fields()
    {
        var label = Sample() with { ItemName = "BAD^NAME~X" };
        var zpl = ZplLabelBuilder.Build(label);
        Assert.Contains("BADNAMEX", zpl);
        Assert.DoesNotContain("BAD^NAME", zpl);
        Assert.EndsWith("^XZ", zpl.TrimEnd());
    }

    [Fact]
    public void Build_renders_null_optional_fields_as_empty()
    {
        var label = Sample() with { ItemName = null, ColorCode = null, CavityPos = null, LineId = null };
        var zpl = ZplLabelBuilder.Build(label);
        Assert.StartsWith("^XA", zpl);
        Assert.EndsWith("^XZ", zpl.TrimEnd());
        Assert.DoesNotContain("^FO260,70", zpl);   // ItemName 줄 생략
    }

    [Fact]
    public void FilePrinter_writes_zpl_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ames-zpl-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var printer = new ZplPrinter(new ZplPrinterOptions { Mode = "File", OutputDir = dir });
            printer.Print("^XA^FDtest^FS^XZ", "LOT-001");
            var file = Path.Combine(dir, "LOT-001.zpl");
            Assert.True(File.Exists(file));
            Assert.Contains("^FDtest^FS", File.ReadAllText(file));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
