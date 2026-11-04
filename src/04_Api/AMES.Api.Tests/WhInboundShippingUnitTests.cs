using System.Runtime.CompilerServices;
using Xunit;

namespace AMES.Api.Tests;

public sealed class WhInboundShippingUnitTests
{
    [Fact]
    public void Document_lookup_accepts_current_and_legacy_shipping_unit_numbers()
    {
        var api = Read("src/04_Api/AMES.Api/Endpoints/WhEndpoints.cs");
        var start = api.IndexOf("private static InboundDocumentResult ExecuteInboundDocument(", StringComparison.Ordinal);
        var end = api.IndexOf("using var conn = factory.OpenConnection();", start, StringComparison.Ordinal);
        var lookup = api[start..end];
        Assert.Contains("StartsWith(\"SHU-\", StringComparison.OrdinalIgnoreCase)", lookup);
        Assert.Contains("|| barcode.Trim().StartsWith(\"CASE-\", StringComparison.OrdinalIgnoreCase)", lookup);
        Assert.Contains("GetDeliveryCase(barcode.Trim()) is { } package", lookup);
        Assert.Contains("if (!package.ReadyToReceive)", lookup);
        Assert.Contains("IsPortalCase: true", lookup);
        Assert.Contains("ApplyCaseReceivePolicy(factory, caseBoxes)", lookup);
    }

    [Fact]
    public void Non_scan_items_are_ready_for_one_atomic_shipping_unit_receipt()
    {
        var page = Page();
        Assert.Contains("IsBoxScanned(box) || (IsCaseDocument && !box.ScanRequired)", page);
        Assert.Contains("_documentBoxes.Count > 0", page);
        Assert.Contains("DisplayScannedBoxes >= _document.TotalBoxes", page);
        Assert.Contains("All remaining unit items are ready. Tap Receive to receive this shipping unit.", page);
        Assert.Contains("Api.WhReceiveCaseAsync(_mode, _document!.DocumentBarcode, _pendingDocumentBoxes)", page);
        Assert.Contains("_documentBoxes.Where(b => !IsBoxReady(b))", page);

        var repository = Read("src/02_Data/AMES.Data/Repositories/ScmDeliveryCases.cs");
        var start = repository.IndexOf("public int ReceiveDeliveryCase(", StringComparison.Ordinal);
        var end = repository.IndexOf("public static void LinkReceivedCase(", start, StringComparison.Ordinal);
        var receipt = repository[start..end];
        Assert.Contains("using var tx=c.BeginTransaction()", receipt);
        Assert.Contains("ISNULL(i.ScanRequired,1)", receipt);
        Assert.Contains("if(r.GetBoolean(1) && !scanned.Contains(barcode))", receipt);
        Assert.Contains("d.Status NOT IN('Shipped','Received')", receipt);
        Assert.Contains("tx.Commit();return boxes.Count;", receipt);
    }

    [Fact]
    public void Inbound_header_has_no_deliveries_shortcut_and_labels_the_shipping_unit()
    {
        var page = Page();
        var header = page[..page.IndexOf("<div class=\"pda-bd", StringComparison.Ordinal)];
        Assert.DoesNotContain("DELIVERIES", header);
        Assert.DoesNotContain("/wh/01", header);
        Assert.Contains("IsCaseDocument ? \"SHIPPING UNIT\" : \"DELIVERY NOTE\"", page);
    }

    static string Page() => Read("src/05_Pda/AMES.Pda/Components/Pages/Wh/Wh02PdaInbound.razor");
    static string Read(string path, [CallerFilePath] string source = "")
        => File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "../../..", path)));
}
