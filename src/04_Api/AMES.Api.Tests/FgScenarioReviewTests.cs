using AMES.Api.Endpoints;
using AMES.Data.Repositories;
using Xunit;

namespace AMES.Api.Tests;

public sealed class FgScenarioReviewTests
{
    [Theory]
    [InlineData(true, true, false, 0, true, null, "awaiting inspection")]
    [InlineData(false, true, false, 0, false, null, "rejected as defective")]
    [InlineData(false, true, true, 1, false, "FG-1", "rejected as defective")]
    [InlineData(false, false, false, 1, false, null, "POP production completion is required")]
    [InlineData(false, false, true, 0, true, null, "already been shipped")]
    [InlineData(false, false, true, 0, false, null, "no available quantity")]
    [InlineData(false, false, true, 2, false, "FG-1", "already stocked at FG-1")]
    [InlineData(false, false, true, 2, false, null, "FG LOT matched. Scan Location.")]
    public void Putaway_reports_the_blocking_reason_before_location_or_quantity_guidance(
        bool inspectionPending, bool rejected, bool completed, int qty, bool shipped, string? location, string expected)
    {
        var message = FgEndpoints.PutAwayScanMessage(inspectionPending, rejected, completed, qty, shipped, location, "Location");
        Assert.Contains(expected, message);
        if (rejected && !inspectionPending) Assert.Contains(message, PdaFinishedGoodsSql.PutAway);
    }

    [Fact]
    public void Scan_and_confirm_prechecks_and_save_guards_preserve_inspection_priority()
    {
        var source = File.ReadAllText(Path.Combine(Root(), "src/04_Api/AMES.Api/Endpoints/FgEndpoints.cs"));
        Assert.Contains("FindOutboundStockError(conn, null, normalized)", source);
        Assert.Contains("FindOutboundStockError(conn, tx, normalized)", source);
        Assert.DoesNotContain("FindOutboundLocationError", source);
        Assert.Contains("if (GetBool(reader, \"InspectionPending\")) return FgOutboundBarcode.InspectionPendingMessage;", source);
        Assert.Contains(FgOutboundBarcode.InspectionPendingMessage, PdaFinishedGoodsSql.OutboundScan);
        Assert.Contains(FgOutboundBarcode.InspectionPendingMessage, PdaFinishedGoodsSql.OutboundComplete);
        Assert.Contains("&& !inspectionPending && !rejectedAsDefective", source);
        var pallet = File.ReadAllText(Path.Combine(Root(), "src/04_Api/AMES.Api/Endpoints/FgPutAwayPallet.cs"));
        Assert.Contains("!item.IsProductionCompleted", pallet);
    }

    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "src/04_Api/AMES.Api"))) return dir.FullName;
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
