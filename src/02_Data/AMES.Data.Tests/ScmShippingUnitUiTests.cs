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
        Assert.Contains("SHIPPING UNIT", label);
        Assert.DoesNotContain("<strong>PALLET</strong>", label);
        Assert.Contains("<Code128Barcode Value=\"@_case.Number\"", label);
        Assert.Contains("Scm.GetDeliveryCase(Number, id)", label);
        Assert.Contains("Shipping Units", Read("src/06_Web/AMES.Web/Components/Pages/Portal/DeliveryNote.razor"));
        var preview = Read("src/06_Web/AMES.Web/Components/Shared/ScmPreview.razor");
        Assert.Contains("OpenCasePreview(item.Number)", preview);
        Assert.Contains("Pages.Portal.DeliveryBoxes", preview);
        Assert.Contains("SCM.Preview.Print.BoxBarcode", preview);
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
        Assert.Contains("Shipping Unit", values["Portal.Create.AddCase"]);
        Assert.Contains("{0}", values["Portal.Create.ReviewFmt"]);
        Assert.Contains("{1}", values["Portal.Create.ReviewFmt"]);
        Assert.DoesNotContain("{2}", values["Portal.Create.ReviewFmt"]);
        Assert.Equal("{0}", System.Text.RegularExpressions.Regex.Match(values["Portal.ShippingUnit.ContentsCount"], @"\{\d+\}").Value);
    }
}
