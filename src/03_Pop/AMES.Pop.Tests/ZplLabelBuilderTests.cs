using AMES.Devices;
using Xunit;

namespace AMES.Pop.Tests;

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
    public void Build_sets_560x320_media_geometry()
    {
        var zpl = ZplLabelBuilder.Build(Sample());
        Assert.Contains("^PW560", zpl);
        Assert.Contains("^LL320", zpl);
        Assert.Contains("^LH10,0", zpl);
        Assert.Contains("^CI28", zpl);    // UTF-8
        Assert.EndsWith("^PQ1\n^XZ", zpl.Replace("\r\n", "\n"));
    }

    [Fact]
    public void Build_matches_the_measured_label_layout_for_a_manual_lot()
    {
        var label = new ZplLabel(
            LotCode: "AAGI10002", ItemNo: "D0101-QI000", ItemName: "", ColorCode: null,
            CavityPos: null, PressType: null, LineId: "LINE-INJ-01",
            ProducedAt: new DateTime(2026, 10, 16, 9, 0, 0));
        var expected = string.Join("\n", new[]
        {
            "^XA",
            "^CI28",
            "^PW560",
            "^LL320",
            "^LH10,0",
            "^FO30,40^BXN,9,200^FDAAGI10002^FS",
            "^FO190,45^A0N,35,35^FD10/16/2026^FS",
            "^FO190,105^A0N,60,60^FDAAGI10002^FS",
            "^FO30,235^A0N,70,70^FDD0101-QI000^FS",
            "^PQ1",
            "^XZ",
        });
        Assert.Equal(expected, ZplLabelBuilder.Build(label).Replace("\r\n", "\n"));
    }

    [Fact]
    public void Build_contains_item_color_cavity_press_and_date()
    {
        var zpl = ZplLabelBuilder.Build(Sample());
        Assert.Contains("^FO30,235^A0N,70,70^FD83335-P8000RBQ^FS", zpl);
        Assert.Contains("^FO190,170^A0N,40,34^FDCBK^FS", zpl);
        Assert.Contains("^FO450,30^A0N,60,48^FDLH^FS", zpl);
        Assert.Contains("^FO450,170^A0N,60,48^FDM^FS", zpl);
        Assert.Contains("^FO190,45^A0N,35,35^FD07/17/2026^FS", zpl);
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
        Assert.DoesNotContain("^FO190,170", zpl);   // 색상 줄 생략
        Assert.DoesNotContain("^FO450,30", zpl);    // 캐비티 줄 생략
        Assert.DoesNotContain("^FO450,170", zpl);   // 호기 줄 생략
        Assert.Contains("^FO30,235^A0N,70,70^FD83335-P8000RBQ^FS", zpl);
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
