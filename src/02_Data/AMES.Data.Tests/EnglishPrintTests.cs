using System.Globalization;
using AMES.Web.Services;
using static AMES.Data.Repositories.ScmRepository;
using Xunit;

namespace AMES.Data.Tests;

public sealed class EnglishPrintTests
{
    [Theory]
    [InlineData("ko-KR")]
    [InlineData("es-ES")]
    public void Documents_use_English_dates_and_original_decimal_units(string culture)
    {
        var previous=CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo(culture);
            Assert.Equal("11/16/2026",EnglishPrint.Date(new DateTime(2026,11,16)));
            Assert.Equal("11/16/2026 13:04",EnglishPrint.DateTime(new DateTime(2026,11,16,13,4,0)));
            Assert.Equal("1,234.125 G",EnglishPrint.Quantity(1234.125m,"g"));
            Assert.Equal("0.125 SH",EnglishPrint.Quantity(.125m,"sh"));
        }
        finally { CultureInfo.CurrentCulture=previous; }
    }

    [Fact]
    public void Complete_label_job_preserves_delivery_note_SHU_and_all_UIT_scan_values_and_quantities()
    {
        var now=new DateTime(2026,11,16);
        var a=new DeliveryBox(1,"UIT-001",1,"PART-1","Resin","g",100.5m,100.5m,"DN-1","PO-1","V-1","LOT-1",now,"Vendor",now,"EOS",2);
        var b=a with {Id=2,Number="UIT-002",Sequence=2,Quantity=20m};
        var unit=new DeliveryCase("SHU-001","DLN-1","V-1","Vendor",now,
            [new(1,a.Number,a.Item,a.Name,a.Unit,a.Quantity,a.Order,"SHU-001",false,1,a.PackingQty)],"PO-1","DN-1","Shipped");
        var note=new DeliveryNote("DLN-1","PO-1","V-1","Vendor","EOS","Warehouse",now,now,"USER",[new(1,a.Item,a.Name,a.Unit,a.Quantity+b.Quantity)]);
        // A loose Unit Item must be printed too, rather than only the SHU children.
        var bundle=new DeliveryPrintBundle(note,[unit],[a,b]);
        Assert.Equal(new[]{"DLN-1","SHU-001","UIT-001","UIT-002"},bundle.AllLabels.Select(l=>l.Barcode));
        Assert.Equal("120.5 G",bundle.AllLabels[0].Quantity);
        Assert.Equal("EOS · Warehouse",bundle.AllLabels[0].PartName);
        Assert.Equal("DELIVERY NOTE",bundle.AllLabels[0].Kind);
        Assert.Equal("100.5 G",bundle.ItemLabels[0].Quantity);
        Assert.Equal("PART-1",bundle.ItemLabels[0].PartNo);
        Assert.Equal("11/16/2026",bundle.ItemLabels[0].Fifo);
        Assert.Equal("LOT-1",bundle.ItemLabels[0].Lot);
        Assert.Equal("PO-1",bundle.ItemLabels[0].Order);
        Assert.Equal("1/2",bundle.ItemLabels[0].Sequence);
        Assert.Equal(new[]{"SUPPLIER","DELIVER TO","DELIVERY DATE"},CompactQrLabelLayout.Fields(bundle.AllLabels[0]).Select(f=>f.Caption));
        Assert.Equal(new[]{"V-1","Warehouse","11/16/2026"},CompactQrLabelLayout.Fields(bundle.AllLabels[0]).Select(f=>f.Value));
        Assert.Equal(new[]{"DLN-1","1"},CompactQrLabelLayout.Fields(bundle.UnitLabels[0]).Select(f=>f.Value));
        Assert.Equal(new[]{"PART NO.","QUANTITY","LOT NO.","FIFO / PRODUCTION DATE"},CompactQrLabelLayout.Fields(bundle.ItemLabels[0]).Select(f=>f.Caption));
        var zpl=string.Concat(bundle.AllLabels.Select(l=>ShippingLabelZpl.Build(l)));
        Assert.Equal(2,zpl.Split("^PQ1").Length-1);
        Assert.Equal(2,zpl.Split("^PQ2").Length-1);
        Assert.Equal(6,bundle.AllLabels.Sum(label=>label.Copies));
        Assert.Equal(4,zpl.Split("^XZ").Length-1);
    }

    [Fact]
    public void Paper_documents_are_English_only_and_repeat_table_headers_on_later_pages()
    {
        foreach(var name in new[]{"PickingOrderDocument","DeliveryNoteDocument"})
        {
            var page=Read($"src/06_Web/AMES.Web/Components/Shared/{name}.razor");
            Assert.Contains("lang=\"en\"",page);
            Assert.DoesNotContain("L[",page);
            Assert.Contains("EnglishPrint.Quantity",page);
            var css=Read($"src/06_Web/AMES.Web/Components/Shared/{name}.razor.css");
            Assert.Contains("display:table-header-group",css);
            Assert.Contains("break-inside:avoid",css);
        }
    }

    [Fact]
    public void Individual_previews_have_one_print_action_for_both_printer_types()
    {
        foreach(var name in new[]{"DeliveryNote","CaseLabel","BoxLabels"})
        {
            var page=Read($"src/06_Web/AMES.Web/Components/Pages/Portal/{name}.razor");
            Assert.Equal(1,System.Text.RegularExpressions.Regex.Matches(page,"<ShippingLabelPrint\\b").Count);
            Assert.DoesNotContain("SCM.Print.All",page);
            Assert.DoesNotContain("Click=\"PrintNote\"",page);
        }
        var note=Read("src/06_Web/AMES.Web/Components/Pages/Portal/DeliveryNote.razor");
        Assert.Contains("LoadLabels=\"ReloadNoteLabels\" BrowserPrint=\"PrintNote\"",note);
        Assert.Contains("Scm.GetDeliveryNote(Number,id)",note);
    }

    [Fact]
    public void Portal_print_menu_asks_for_output_method_before_connecting_a_printer()
    {
        var page=Read("src/06_Web/AMES.Web/Components/Shared/ScmPreview.razor");
        Assert.Contains("SCM.Print.All",page);
        Assert.Contains("BrowserPrint=\"()=>OpenPrintPacket(documentNumber)\"",page);
        Assert.Contains("LoadLabels=\"ReloadPrintingLabels\"",page);
        var packet=Read("src/06_Web/AMES.Web/Components/Pages/Portal/DeliveryPrint.razor");
        Assert.Contains("PortalAuth.IsPortalUser",packet);
        Assert.Contains("DeliveryPrintBundle.Load",packet);
        Assert.Contains("<DeliveryNoteDocument",packet);
        Assert.Contains("Labels=\"bundle.UnitLabels\"",packet);
        Assert.Contains("<BoxLabelSheet Boxes=\"bundle.UnitItems\"",packet);
        Assert.Contains("await ReloadLabels(); _printRequested=true",packet);
        var chooser=Read("src/06_Web/AMES.Web/Components/Shared/ShippingLabelPrint.razor");
        Assert.Contains("Click=\"OpenChoice\"",chooser);
        Assert.Contains("Print.ZebraPrinter",chooser);
        Assert.Contains("Print.GeneralPrinter",chooser);
        Assert.Contains("async Task SelectZebra()",chooser);
        Assert.Contains("<CompactQrLabelSheet Label=\"_previewLabels[_previewIndex]\"",chooser);
        Assert.Contains("labels.SequenceEqual(_previewLabels)",chooser);
        Assert.Contains("async Task SelectBrowser(){_open=false;await BrowserPrint.InvokeAsync();}",chooser);
        Assert.DoesNotContain("OnAfterRenderAsync",chooser);
    }

    private static string Read(string path,[System.Runtime.CompilerServices.CallerFilePath]string current="")=>
        File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(current)!,"../../..",path)));
}
