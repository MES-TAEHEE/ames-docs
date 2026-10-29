using AMES.Data.Repositories;
using Xunit;
using HistoryPage = AMES.Web.Components.Pages.Wh.LogHistory;

namespace AMES.Data.Tests;

public sealed class WarehouseHistoryDisplayTests
{
    [Fact]
    public void Receipt_cancellation_is_in_all_events_but_not_in_receipt_or_adjustment_counts()
    {
        WarehouseRepository.OperationLogRow[] rows =
        [
            Row("RECEIVE"), Row("RECEIVE"), Row("CANCEL_RECEIPT"), Row("RELEASE_PICK"),
            Row("ADJUST_SAVE"), Row("ADJUST_SAVE", "Defective quantity"), Row("MOVE_LOCATION"),
        ];

        Assert.Equal(7, rows.Length);
        Assert.Equal(2, HistoryPage.CountEvents(rows, "INBOUND"));
        Assert.Equal(1, HistoryPage.CountEvents(rows, "OUTBOUND"));
        Assert.Equal(2, HistoryPage.CountEvents(rows, "ADJUSTMENT"));
        Assert.Equal(0, HistoryPage.CountEvents([Row("MOVE_LOCATION")], "INBOUND"));
    }

    [Theory]
    [InlineData("CANCEL_RECEIPT", null, "WH.LogHistory.Lbl.CancelInbound")]
    [InlineData("STATUS_CHANGE", "RECEIVED -> RECEIPT_CANCELLED", "WH.LogHistory.Lbl.InboundCancelled")]
    public void Both_cancellation_labels_are_excluded_from_KPIs(string type, string? message, string label)
    {
        var row = Row(type, message);
        Assert.Equal(label, HistoryPage.DisplayEvent(row));
        Assert.Equal("bad", HistoryPage.EventClass(row));
        foreach (var kpi in new[] { "INBOUND", "OUTBOUND", "ADJUSTMENT" })
            Assert.Equal(0, HistoryPage.CountEvents([row], kpi));
    }

    [Theory]
    [InlineData("RECEIVED")]
    [InlineData("STORED")]
    [InlineData("RETURN_RECEIVED")]
    public void Existing_receipt_status_labels_are_still_counted(string status)
        => Assert.Equal(1, HistoryPage.CountEvents([Row("STATUS_CHANGE", "CREATED -> " + status)], "INBOUND"));

    [Fact]
    public void Page_uses_the_tested_counter_and_badge_mapping()
    {
        var page = Read("src/06_Web/AMES.Web/Components/Pages/Wh/LogHistory.razor");
        Assert.Contains("int CountEvent(string type) => CountEvents(_rows, type);", page);
        Assert.Contains("class=\"pill @EventClass(r)\"", page);
    }

    static WarehouseRepository.OperationLogRow Row(string type, string? message = null)
        => new(1, null, type, null, null, null, null, null, null, null, null, null,
            "SUCCESS", message, null, null, null, null, null, null, null);

    static string Read(string path, [System.Runtime.CompilerServices.CallerFilePath] string file = "")
        => File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../../..", path)));
}
