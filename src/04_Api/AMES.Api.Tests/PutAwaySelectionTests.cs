using AMES.Api.Endpoints;
using Xunit;

namespace AMES.Api.Tests;

public class PutAwaySelectionTests
{
    private static WhEndpoints.PutAwayRow Row(string barcode, string? location = null) =>
        new("LOCAL", barcode, barcode, "PART-1", "Part", 10, "EA", "DLN-1", null, location, "CASE-1");

    [Fact]
    public void BoxScanReturnsOnlyScannedBox()
    {
        var selection = WhEndpoints.BuildPutAwaySelection("BOX-1", [Row("BOX-1"), Row("BOX-2")]);

        Assert.NotNull(selection);
        Assert.Equal("BOX", selection.SelectionType);
        Assert.Equal(["BOX-1"], selection.Boxes.Select(box => box.Barcode));
        Assert.Equal(["BOX-1"], selection.SelectedBarcodes);
    }

    [Fact]
    public void StoredBoxStillRequiresRelocationConfirmation()
    {
        var selection = WhEndpoints.BuildPutAwaySelection("BOX-2", [Row("BOX-2", "MA01A1F1")]);

        Assert.NotNull(selection);
        Assert.Equal("BOX", selection.SelectionType);
        Assert.True(selection.RequiresRelocation);
        Assert.Equal(["BOX-2"], selection.SelectedBarcodes);
    }

    [Fact]
    public void CaseScanSelectsOnlyPendingBoxes()
    {
        var selection = WhEndpoints.BuildPutAwaySelection("CASE-1", [Row("BOX-1"), Row("BOX-2", "MA01A1F1")]);

        Assert.NotNull(selection);
        Assert.Equal("CASE", selection.SelectionType);
        Assert.Equal(2, selection.Boxes.Count);
        Assert.Equal(["BOX-1"], selection.SelectedBarcodes);
        Assert.False(selection.RequiresRelocation);
        Assert.Equal("MA01A1F1", selection.Boxes[1].LocationNo);
    }

    [Fact]
    public void DeliveryNoteScanIsViewOnly()
    {
        var selection = WhEndpoints.BuildPutAwaySelection("DLN-1", [Row("BOX-1"), Row("BOX-2", "MA01A1F1")]);

        Assert.NotNull(selection);
        Assert.Equal("DELIVERY_NOTE", selection.SelectionType);
        Assert.Equal(2, selection.Boxes.Count);
        Assert.Empty(selection.SelectedBarcodes);
        Assert.False(selection.RequiresRelocation);
    }
}
