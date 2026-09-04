using AMES.Devices;
using Xunit;

namespace AMES.Pop.Tests;

public class ImgLabelBuilderTests
{
    static ImgLabel Sample() => new(
        LotCode:      "A94W10002",
        ItemNo:       "83345-P8000RBQ",
        CustomerCode: "BYU5",
        Pgn:          "8132",
        Alc:          "8046",
        MountPos:     "RR",
        ShiftLetter:  "A",
        IssuedAt:     new DateTime(2026, 9, 4, 8, 1, 8));

    [Fact]
    public void Builds_customer_datamatrix_and_human_readable_lines()
    {
        var zpl = ImgLabelBuilder.Build(Sample());

        var expected = string.Join("\n", new[]
        {
            "^XA",
            "^PW812",
            "^LL406",
            "^LH0,0",
            "^CI28",
            "^FO36,68^BXN,7,200^FH_^FD[)>_1E06_1DVBYU5_1DP83345P8000RBQ_1DS81328046_1DT2609041P8AA94W10002_1DE_1DC:_1E_04^FS",
            "^FO260,50^A0N,124,96^FD8046^FS",
            "^FO500,50^A0N,80,64^FDRR^FS",
            "^FO260,160^A0N,58,46^FD9/4/2026^FS",
            "^FO260,220^A0N,78,56^FD83345-P8000RBQ^FS",
            "^FO260,300^A0N,78,56^FDA94W10002^FS",
            "^XZ",
        });
        Assert.Equal(expected, zpl.Replace("\r\n", "\n"));
    }

    [Fact]
    public void Part4M_uses_6th_and_7th_chars_of_hyphenless_item_no_and_shift_letter()
    {
        Assert.Equal("1P8B", ImgLabelBuilder.Part4M("83345-P8000RBQ", "B"));
        Assert.Equal("1LHC", ImgLabelBuilder.Part4M("DR-TRM-LH-W", "C"));   // DRTRMLHW → 6th 'L', 7th 'H'
    }

    [Fact]
    public void Part4M_short_item_no_does_not_throw()
    {
        Assert.Equal("1ABA", ImgLabelBuilder.Part4M("12345AB", "A"));
        Assert.Equal("1A",   ImgLabelBuilder.Part4M("12345A", ""));
    }

    [Fact]
    public void Missing_optional_fields_leave_their_lines_out_but_keep_datamatrix_tokens()
    {
        var l = Sample() with { Alc = null, MountPos = null, Pgn = null, CustomerCode = null };
        var zpl = ImgLabelBuilder.Build(l);

        Assert.Contains("_1DV_1DP83345P8000RBQ_1DS_1DT", zpl);
        Assert.DoesNotContain("^FO260,50", zpl);
        Assert.DoesNotContain("^FO500,50", zpl);
        Assert.Contains("^FO260,300^A0N,78,56^FDA94W10002^FS", zpl);
    }

    [Fact]
    public void Zpl_control_and_escape_chars_are_stripped_from_data()
    {
        var l = Sample() with { ItemNo = "AB^C~D_E-F" };
        var zpl = ImgLabelBuilder.Build(l);
        Assert.Contains("_1DPABCDEF_1D", zpl);
        Assert.Contains("^FDABCDE-F^FS", zpl);
    }
}
