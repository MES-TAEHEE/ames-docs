using System.Xml.Linq;
using Xunit;

namespace AMES.Data.Tests;

public sealed class ScmShippingUnitUiTests
{
    private static string Root([System.Runtime.CompilerServices.CallerFilePath] string path = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "../../.."));
    private static string Read(string path) => File.ReadAllText(Path.Combine(Root(), path));

    [Fact]
    public void Creation_has_one_group_action_and_preserves_quantity_and_destination_checks()
    {
        var page = Read("src/06_Web/AMES.Web/Components/Pages/Portal/DeliveryCases.razor");
        Assert.Contains("Click=\"AddShippingUnit\"", page);
        foreach (var removed in new[] { "Portal.Create.AddBox", "Portal.Create.AddPart", "_editingKind", "LooseDraft", "_loose", "KindLabel" })
            Assert.DoesNotContain(removed, page);
        Assert.Contains("_activeCase=_draft.Where(b=>b.Selected).ToList();_draftCases.Add(_activeCase)", page);
        Assert.Contains("row.Part.PackingQty", page);
        Assert.Contains("row.Part.Issue.Length==0", page);
        Assert.Contains("p.Quantity<=Available(p)", page);
        Assert.Contains("first.Vendor==order.Vendor && first.Destination==order.Destination", page);
        Assert.Contains("Scm.CreateDeliveryNoteFromDraftCases(cases,date,_user,_actor)", page);
        Assert.Contains("_draftCases.SelectMany(c=>c)", page);
        Assert.Contains(">1000", page);
        Assert.Contains("data-no-busy", page);
    }

    [Fact]
    public void Print_keeps_group_and_child_barcode_paths_with_shipping_unit_labels()
    {
        var label = Read("src/06_Web/AMES.Web/Components/Pages/Portal/CaseLabel.razor");
        Assert.Contains("SHIPPING UNIT (SHU)", label);
        Assert.DoesNotContain("<strong>PALLET</strong>", label);
        Assert.Contains("<Code128Barcode Value=\"@_case.Number\"", label);
        Assert.Contains("Scm.GetDeliveryCase(Number, id)", label);
        Assert.Contains("Shipping Units", Read("src/06_Web/AMES.Web/Components/Pages/Portal/DeliveryNote.razor"));
        var preview = Read("src/06_Web/AMES.Web/Components/Shared/ScmPreview.razor");
        Assert.Contains("OpenCasePreview(item.Number)", preview);
        Assert.Contains("Pages.Portal.DeliveryBoxes", preview);
        Assert.Contains("SCM.Preview.Print.BoxBarcode", preview);
        var childLabel = Read("src/06_Web/AMES.Web/Components/Shared/BoxLabelSheet.razor");
        Assert.Contains("UNIT ITEM (UIT)", childLabel);
        Assert.Contains("<Code128Barcode Value=\"@b.Number\"", childLabel);
        Assert.DoesNotContain("StartsWith(\"PT-\"", childLabel);
        Assert.DoesNotContain("\"BOX\"", childLabel);
        Assert.Contains("<th>Unit Items</th>", Read("src/06_Web/AMES.Web/Components/Pages/Portal/DeliveryNote.razor"));
    }

    [Fact]
    public void New_barcodes_use_shu_and_uit_without_replacing_saved_labels()
    {
        var groups = Read("src/02_Data/AMES.Data/Repositories/ScmDeliveryCases.cs");
        var items = Read("src/02_Data/AMES.Data/Repositories/ScmDeliveryBoxes.cs");
        Assert.Contains("CONCAT('SHU-'", groups);
        Assert.Contains("NEXT VALUE FOR dbo.SCM_CaseNumberSequence", groups);
        Assert.Contains("$\"UIT-{vendor}-{date:yyyyMMdd}", groups);
        Assert.Contains("$\"UIT-{vendor}-{numberDate.ToString", items);
        Assert.Contains("$\"UIT-{vendor}-{day:yyyyMMdd}", items);
        Assert.Contains("WITH(UPDLOCK,HOLDLOCK)", items);
        Assert.Contains("if(l.SavedQty==l.Qty && l.Count>0)continue;", items);
        Assert.DoesNotContain("CONCAT('CASE-'", groups);
        Assert.DoesNotContain("$\"BX-", groups + items);
    }

    [Theory]
    [InlineData("SharedResources.resx")]
    [InlineData("SharedResources.en.resx")]
    [InlineData("SharedResources.ko.resx")]
    [InlineData("SharedResources.es.resx")]
    public void Group_name_is_consistent_and_review_format_has_only_used_arguments(string file)
    {
        var xml = XDocument.Parse(Read("src/06_Web/AMES.Web/Resources/" + file));
        var values = xml.Root!.Elements("data").ToDictionary(x => x.Attribute("name")!.Value, x => x.Element("value")!.Value);
        Assert.Equal("Shipping Unit", values["SCM.Preview.Lbl.Case"]);
        Assert.Equal("Unit Item Barcode", values["SCM.Preview.Print.BoxBarcode"]);
        Assert.Equal("Unit Item Barcode Printing", values["Portal.Text.BoxBarcodePrinting"]);
        Assert.Equal("Unit Item Labels", values["Portal.Text.BoxLabels"]);
        Assert.Equal("Unit Items", values["Portal.Boxes.Title"]);
        Assert.Equal("Unit Item Barcode", values["Portal.Boxes.Col.Barcode"]);
        Assert.Equal("Unit Item", values["Portal.Create.Box"]);
        Assert.Equal("Unit Items", values["Portal.Create.Col.BoxesParts"]);
        Assert.Equal("Unit Items Needed", values["Portal.Create.Col.BoxesNeeded"]);
        Assert.Equal("Select Unit Items", values["Portal.Create.Step2.Boxes"]);
        Assert.Contains("Shipping Unit", values["Portal.Create.AddCase"]);
        Assert.Contains("{0}", values["Portal.Create.ReviewFmt"]);
        Assert.Contains("{1}", values["Portal.Create.ReviewFmt"]);
        Assert.DoesNotContain("{2}", values["Portal.Create.ReviewFmt"]);
        Assert.Equal("{0}", System.Text.RegularExpressions.Regex.Match(values["Portal.ShippingUnit.ContentsCount"], @"\{\d+\}").Value);
    }
}
