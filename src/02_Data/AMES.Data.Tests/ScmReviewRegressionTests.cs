using AMES.Data.Connection;
using AMES.Data.Repositories;
using Xunit;

namespace AMES.Data.Tests;

public sealed class ScmReviewRegressionTests
{
    [Theory]
    [InlineData("Draft", false, 0, 0, 0, "Draft")]
    [InlineData("Cancelled", true, 10, 10, 10, "Cancelled")]
    [InlineData("Open", false, 0, 0, 0, "AwaitingConfirmation")]
    [InlineData("Open", true, 0, 0, 0, "SupplierConfirmed")]
    [InlineData("Open", true, 0, 0, 4, "PreparingDelivery")]
    [InlineData("Open", true, 4, 0, 4, "Shipped")]
    [InlineData("Partial", true, 10, 4, 10, "PartiallyReceived")]
    [InlineData("Complete", true, 10, 10, 10, "Received")]
    public void Progress_filter_and_display_use_the_same_status(string status,bool confirmed,
        int shipped,int received,int registered,string expected)
        => Assert.Equal(expected,ScmRepository.ProgressOrderStatus(status,confirmed,[(10,shipped,received,registered)]));

    [Fact]
    public void Receiving_one_line_does_not_close_the_whole_order()
        => Assert.Equal("PartiallyReceived",ScmRepository.ProgressOrderStatus("Partial",true,[(10,10,10,10),(5,5,0,5)]));

    [SkippableFact]
    public void Filtered_orders_and_deliveries_load_without_database_writes()
    {
        var connection=Environment.GetEnvironmentVariable("AMES_TEST_CONN");
        Skip.If(string.IsNullOrWhiteSpace(connection),"Set AMES_TEST_CONN for read-only SCM query verification.");
        var repo=new ScmRepository(new AmesConnectionFactory(connection!));
        var all=repo.ListPurchaseOrders();
        if(all.Count==0)return;
        var order=all[0];
        var filtered=repo.ListPurchaseOrders(filter:new(Search:order.Number));
        Assert.Contains(filtered,x=>x.Number==order.Number);
        Assert.All(repo.ListPurchaseOrders(filter:new(Vendor:order.Vendor)),x=>Assert.True(x.Vendor==order.Vendor || x.DataIssue is not null));
        if(order.Due.Year>=1900)
            Assert.Contains(repo.ListPurchaseOrders(filter:new(From:order.Due.Date,To:order.Due.Date)),x=>x.Number==order.Number);
        var ids=filtered.SelectMany(x=>x.Lines).Select(x=>x.PoID).ToArray();
        Assert.All(repo.ListSupplierDeliveries(false,null,ids),x=>Assert.Contains(x.PoID,ids));
        Assert.Empty(repo.ListSupplierDeliveries(false,null,Array.Empty<int>()));
        var line=order.Lines[0];
        repo.FindPurchasePrice(order.Vendor,line.Item,line.Unit);
    }
}
