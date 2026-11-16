using Xunit;

namespace AMES.Data.Tests;

public sealed class PickingOrdersDisplayTests
{
    [Fact]
    public void Picking_order_details_print_a_material_request_without_a_barcode()
    {
        var page = Read("src/06_Web/AMES.Web/Components/Pages/Wh/PickingOrders.razor");
        Assert.Contains("RowSelect=\"SelectOrder\"", page);
        Assert.Contains("WH.ListPickingSlipLines(row.PickSlipNo)", page);
        Assert.Contains("WH.ListPickingSlipFifoLots(row.PickSlipNo)", page);
        Assert.Contains("@SourceText(row)", page);
        Assert.Contains("Property=\"LineCode\"", page);
        Assert.Contains("@L[\"Btn.Close\"].Value", page);
        Assert.Contains("Click=\"PrintOrder\"",page);
        Assert.Contains("<PickingOrderDocument Header=\"_selected\"",page);
        Assert.Contains("\"amesPrintLabels\",\".picking-order-document\",\"A4 portrait\",true",page);
        foreach (var removed in new[] { "Code128Barcode", "PrintBarcode", "wh-slip-print-area", "BuildPrintRows", "window.print" })
            Assert.DoesNotContain(removed, page);
        foreach (var path in new[] { "src/06_Web/AMES.Web/Components/Pages/Wh/PickingOrders.razor.css", "src/06_Web/AMES.Web/wwwroot/app.css" })
            Assert.DoesNotContain("wh-slip-print-area", Read(path));
    }

    private static string Read(string path, [System.Runtime.CompilerServices.CallerFilePath] string current = "")
        => File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(current)!, "../../..", path)));
}
