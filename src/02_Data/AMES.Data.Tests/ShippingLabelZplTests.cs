using AMES.Data.Repositories;
using AMES.Web.Services;
using Xunit;

namespace AMES.Data.Tests;

public sealed class ShippingLabelZplTests
{
    [Theory]
    [InlineData(203,799,320)]
    [InlineData(300,1181,472)]
    public void Zebra_labels_use_the_approved_fixed_size_and_one_copy(int dpi, int width, int height)
    {
        var zpl = ShippingLabelZpl.Build(new("UIT-1","UNIT ITEM (UIT)","Vendor ^XZ","PART-1","RESIN","0.1 kg","LOT 123"),dpi);
        Assert.Contains($"^PW{width}\n^LL{height}",zpl);
        Assert.Contains("^PQ1\n^XZ",zpl);
        Assert.Equal(1,zpl.Split("^XA").Length-1);
        Assert.Equal(1,zpl.Split("^XZ").Length-1);
        Assert.Contains("_55_49_54_2D_31",zpl);
        Assert.Contains("_50_41_52_54_2D_31",zpl);
        Assert.DoesNotContain("Vendor ^XZ",zpl);
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid\nbarcode")]
    [InlineData("with space")]
    public void Invalid_scan_values_are_rejected_before_printing(string barcode)
        => Assert.Throws<ArgumentException>(()=>ShippingLabelZpl.Build(new(barcode,"SHU","Vendor","","","","")));

    [Theory]
    [InlineData("DELIVERY NOTE","DLN-20260925-6","",203,1)]
    [InlineData("SHIPPING UNIT (SHU)","SHU-VENDOR-20261116-0001","",203,1)]
    [InlineData("UNIT ITEM (UIT)","UIT-LP-DAEHA-20261021-0010","21358G2-PIDHA",203,1)]
    [InlineData("DELIVERY NOTE","DLN-20260925-6","",300,1)]
    [InlineData("SHIPPING UNIT (SHU)","SHU-VENDOR-20261116-0001","",300,1)]
    [InlineData("UNIT ITEM (UIT)","UIT-LP-DAEHA-20261021-0010","21358G2-PIDHA",300,1)]
    public void Every_document_uses_one_QR_with_only_its_original_scan_value(string kind,string barcode,string part,int dpi,int barcodeCount)
    {
        var layout=new CompactQrLabelLayout(dpi);
        var zpl=ShippingLabelZpl.Build(new(barcode,kind,"Vendor",part,"Resin","100 G","LOT 1"),dpi);
        Assert.DoesNotContain("^BC",zpl);
        Assert.Equal(barcodeCount,zpl.Split("^BQN").Length-1);
        var encoded=string.Concat(System.Text.Encoding.UTF8.GetBytes(barcode).Select(b=>$"_{b:X2}"));
        Assert.Contains($"^FH^FDLM,B{barcode.Length:D4}{encoded}^FS",zpl);
        Assert.InRange(layout.QrSideMm(barcode),1m,24m);
        Assert.True(layout.QrLeftMm(barcode)>0);
        Assert.True(layout.QrLeftMm(barcode)+layout.QrSideMm(barcode)<CompactQrLabelLayout.TextLeftMm);
        Assert.Contains($"^FO{layout.QrX(barcode)},{layout.QrY(barcode)}^BQN",zpl);
    }

    [Fact]
    public void Text_that_would_overflow_is_rejected_instead_of_clipped()
    {
        var label=new ShippingLabel("UIT-1","UIT","Vendor","PART-1","Resin","100 G","",Lot:new string('W',80));
        Assert.Throws<ArgumentException>(()=>ShippingLabelZpl.Build(label,203));
        Assert.Throws<ArgumentException>(()=>ShippingLabelZpl.Build(label,300));
        var layout=new CompactQrLabelLayout();
        Assert.InRange(layout.QrSideMm(new string('A',80)),1m,24m);
    }

}
