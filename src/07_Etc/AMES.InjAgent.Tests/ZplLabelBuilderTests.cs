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
        PressType:  "M",
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
    public void Build_sets_2x1_inch_media_geometry()
    {
        var zpl = ZplLabelBuilder.Build(Sample());
        Assert.Contains("^PW406", zpl);   // 2" @ 203dpi
        Assert.Contains("^LL203", zpl);   // 1" @ 203dpi
        Assert.Contains("^CI28", zpl);    // UTF-8
    }

    [Fact]
    public void Build_contains_item_color_cavity_press_and_date()
    {
        var zpl = ZplLabelBuilder.Build(Sample());
        Assert.Contains("^FO20,138^A0N,50,39^FD83335-P8000RBQ^FS", zpl);
        Assert.Contains("^FO140,82^A0N,31,27^FDCBK^FS", zpl);
        Assert.Contains("^FO320,14^A0N,43,34^FDLH^FS", zpl);
        Assert.Contains("^FO320,73^A0N,43,34^FDM^FS", zpl);
        Assert.Contains("^FO140,14^A0N,30,27^FD07/17/2026^FS", zpl);
    }

    [Fact]
    public void Build_strips_zpl_control_chars_from_fields()
    {
        var label = Sample() with { ItemNo = "BAD^NO~X" };
        var zpl = ZplLabelBuilder.Build(label);
        Assert.Contains("BADNOX", zpl);
        Assert.DoesNotContain("BAD^NO", zpl);
        Assert.EndsWith("^XZ", zpl.TrimEnd());
    }

    [Fact]
    public void Build_omits_lines_for_null_optional_fields()
    {
        // 수동 발행 LOT — PressType 이 NULL 이라 호기 줄이 없어야 한다.
        var label = Sample() with { ColorCode = null, CavityPos = null, PressType = null };
        var zpl = ZplLabelBuilder.Build(label);
        Assert.StartsWith("^XA", zpl);
        Assert.EndsWith("^XZ", zpl.TrimEnd());
        Assert.DoesNotContain("^FO140,82", zpl);   // 색상 줄 생략
        Assert.DoesNotContain("^FO320,14", zpl);   // 캐비티 줄 생략
        Assert.DoesNotContain("^FO320,73", zpl);   // 호기 줄 생략
        Assert.Contains("^FO20,138^A0N,50,39^FD83335-P8000RBQ^FS", zpl);
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
