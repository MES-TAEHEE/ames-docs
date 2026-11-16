using AMES.Data.Repositories;
using AMES.Web.Services;
using Xunit;

namespace AMES.Data.Tests;

public sealed class PickingReleaseHistoryTests
{
    [Fact]
    public void Release_history_is_actual_picking_transactions_including_partial_orders()
    {
        var sql = WarehouseRepository.PickingReleaseHistorySql;
        Assert.Contains("t.TransactionType='OUT' AND t.SourceType='PICK_SLIP'",sql);
        Assert.Contains("ABS(t.QtyChange)",sql);
        Assert.Contains("t.TransactionTime>=@From AND t.TransactionTime<@Until",sql);
        Assert.DoesNotContain("p.Status",sql);
        Assert.DoesNotContain("WH_Inventory ",sql);
    }

    [SkippableFact]
    public void Released_items_read_the_existing_database_without_writes()
    {
        var connection=Environment.GetEnvironmentVariable("AMES_TEST_CONN");
        Skip.If(string.IsNullOrWhiteSpace(connection),"Set AMES_TEST_CONN for read-only verification.");
        var repository=new WarehouseRepository(new AMES.Data.Connection.AmesConnectionFactory(connection!));
        var from=DateTime.Today.AddMonths(-1);var to=DateTime.Today.AddMonths(1);
        var rows=repository.ListPickingReleases(from,to);
        Assert.All(rows,r=>Assert.InRange(r.ReleasedAt,from,to.AddDays(1)));
        Assert.All(rows,r=>Assert.True(r.Quantity>=0));
    }
    [Fact]
    public void Picking_releases_are_a_peer_tab_between_orders_and_line_stock()
    {
        var page=File.ReadAllText(Path.Combine(Root(),"src/06_Web/AMES.Web/Components/Pages/Wh/PickingOrders.razor"));
        var tabs=System.Text.RegularExpressions.Regex.Matches(page,"<AmesTab Title=([^>]+)>").Select(m=>m.Value).ToArray();
        Assert.Equal(3,tabs.Length);
        Assert.Contains("WH.Picking.Title",tabs[0]);
        Assert.Contains("WH.Picking.Releases",tabs[1]);
        Assert.Contains("WH.Picking.Tab.LineStock",tabs[2]);
    }

    private static string Root([System.Runtime.CompilerServices.CallerFilePath] string path="") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!,"../../.."));
}
