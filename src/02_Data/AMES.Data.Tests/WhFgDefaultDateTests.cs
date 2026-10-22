using Xunit;

namespace AMES.Data.Tests;

public sealed class WhFgDefaultDateTests
{
    [Theory]
    [InlineData("Pages/Wh/LogHistory.razor")]
    [InlineData("Pages/Fg/ShippingOrders.razor")]
    [InlineData("Pages/Fg/ShipmentPlan.razor")]
    [InlineData("Pages/Fg/Shipments.razor")]
    [InlineData("Pages/Fg/CustomerReturns.razor")]
    [InlineData("Pages/Fg/History.razor")]
    [InlineData("Shared/ShipmentOrderCreate.razor")]
    public void Web_date_filters_use_calendar_months_on_both_sides_of_database_today(string component)
    {
        var source = Read("src/06_Web/AMES.Web/Components/" + component);
        Assert.Contains("DbClock.Today.AddMonths(-1)", source);
        Assert.Contains("DbClock.Today.AddMonths(1)", source);
        Assert.DoesNotContain("DbClock.Today.AddDays(6)", source);
        Assert.DoesNotContain("DbClock.Today.AddDays(-90)", source);
        Assert.DoesNotContain("DbClock.Today.AddMonths(-6)", source);
        Assert.DoesNotContain("DateTime.Today", source);
    }

    [Fact]
    public void Web_resets_restore_the_range_and_plan_links_keep_explicit_dates()
    {
        foreach(var component in new[]{"Wh/LogHistory", "Fg/Shipments", "Fg/CustomerReturns", "Fg/History"})
        {
            var source=Read("src/06_Web/AMES.Web/Components/Pages/"+component+".razor");
            Assert.Equal(2,source.Split("DbClock.Today.AddMonths(-1)").Length-1);
            Assert.Equal(2,source.Split("DbClock.Today.AddMonths(1)").Length-1);
        }
        var plan=Read("src/06_Web/AMES.Web/Components/Pages/Fg/ShipmentPlan.razor");
        Assert.Contains("From?.Date??DbClock.Today.AddMonths(-1)",plan);
        Assert.Contains("To?.Date??DbClock.Today.AddMonths(1)",plan);
        Assert.Contains("_shipDate=DbClock.Today;",Read("src/06_Web/AMES.Web/Components/Shared/ShipmentOrderCreate.razor"));
    }

    [Fact]
    public void Pda_wh_and_fg_dates_use_database_time_without_overriding_spare_parts_or_explicit_filters()
    {
        var deliveries=Read("src/05_Pda/AMES.Pda/Components/Pages/Wh/Wh01DeliverySchedule.razor");
        Assert.Contains("var today = (await Api.DatabaseTimeAsync()).Date",deliveries);
        Assert.Contains("_from = today.AddMonths(-1); _to = today.AddMonths(1)",deliveries);
        var history=Read("src/05_Pda/AMES.Pda/Components/Pages/Wh/Wh08TransactionHistory.razor");
        Assert.Contains("_dateFrom ??= IsSpareParts ? _databaseToday.AddDays(-30) : _databaseToday.AddMonths(-1)",history);
        Assert.Contains("_dateTo ??= IsSpareParts ? _databaseToday : _databaseToday.AddMonths(1)",history);
        var inventory=Read("src/05_Pda/AMES.Pda/Components/Pages/Wh/Wh03InventoryStatus.razor");
        Assert.Contains("if (!IsSpareParts && !IsAdjustTab)",inventory);
        Assert.Contains("_dateFrom = today.AddMonths(-1)",inventory);
        Assert.Contains("_dateTo = today.AddMonths(1)",inventory);
        var clear=inventory[inventory.IndexOf("private async Task ClearDateFilter()",StringComparison.Ordinal)..];
        Assert.Contains("_dateFrom = null;",clear);
        Assert.Contains("_dateTo = null;",clear);
        var endpoint=Read("src/04_Api/AMES.Api/Endpoints/SupplierDeliveryEndpoints.cs");
        Assert.Contains("from ??= DbClock.Today.AddMonths(-1)",endpoint);
        Assert.Contains("to ??= DbClock.Today.AddMonths(1)",endpoint);
    }

    private static string Read(string path,[System.Runtime.CompilerServices.CallerFilePath]string current="")
        =>File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(current)!,"../../..",path)));
}
