using AMES.Data.Connection;
using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.Data.Tests;

public sealed class FgHistoryRowKeyTests
{
    [Fact]
    public void Identical_visible_events_still_have_distinct_record_and_grid_keys()
    {
        var first=new FinishedGoodsRepository.HistoryRow("TX:1",new(2026,10,28),"PUT AWAY","1","PART",1,"LOC","USER","Confirmed",null);
        var second=first with { RowKey="TX:2" };
        Assert.NotEqual(first,second);
        Assert.NotEqual(first.RowKey,second.RowKey);
        var page=File.ReadAllText(Path.Combine(Root(),"src/06_Web/AMES.Web/Components/Pages/Fg/History.razor"));
        Assert.Contains("KeyProperty=\"RowKey\"",page);
    }

    [Theory]
    [InlineData("SHIPPED",170,true)]
    [InlineData("SHIPPED",null,false)]
    [InlineData("PUT AWAY",170,false)]
    [InlineData("RETURN",170,false)]
    public void Only_shipment_events_offer_pallet_part_and_LOT_details(string type,int? shipmentId,bool expandable)
    {
        var row=new FinishedGoodsRepository.HistoryRow("DCR:1",new(2026,10,28),type,"DCR-1",null,140,null,"USER",type,null,
            DeliveryId:1,ShipmentId:shipmentId,PalletCount:6,ItemCount:140);
        Assert.Equal(expandable,row.HasShipmentDetails);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    public void Shipment_history_rejects_unsafe_limits_before_accessing_the_database(int limit)
    {
        var repo=new FinishedGoodsRepository(new AmesConnectionFactory("invalid"));
        Assert.Throws<ArgumentOutOfRangeException>(()=>repo.ListShipmentHistory(limit:limit));
    }

    [Fact]
    public void History_reuses_shipment_drilldowns_and_keeps_queries_lazy_and_modal_results_current()
    {
        var page=File.ReadAllText(Path.Combine(Root(),"src/06_Web/AMES.Web/Components/Pages/Fg/History.razor"));
        Assert.Contains("RowExpand=\"LoadChildren\"",page);
        Assert.Contains("ShowExpandColumn=\"false\"",page);
        Assert.Contains("row.HasShipmentDetails",page);
        Assert.Contains("IsExpanded(row) ? \"expand_more\" : \"chevron_right\"",page);
        Assert.Contains("_children.ContainsKey(row.RowKey)",page);
        Assert.Contains("ListShipmentHistoryParts(row.DeliveryId,row.ShipmentId!.Value)",page);
        Assert.Contains("OpenLots(history,item)",page);
        Assert.Contains("ListShipmentHistoryLots(history.DeliveryId,history.ShipmentId!.Value,part.PalletNo,part.PartNo)",page);
        Assert.Contains("KeyProperty=\"TransactionId\"",page);
        Assert.Contains("request==_lotRequest",page);
        Assert.Contains("request==_reloadRequest",page);
        Assert.Contains("void CloseLots(){++_lotRequest",page);
    }

    [Fact]
    public void History_details_use_a_bounded_preview_and_a_wrapping_closeable_modal()
    {
        var path=Path.Combine(Root(),"src/06_Web/AMES.Web/Components/Pages/Fg/History.razor");
        var page=File.ReadAllText(path);
        var css=File.ReadAllText(path+".css");
        Assert.Contains("Title=\"@L[\"Col.Details\"].Value\" Width=\"240px\" MinWidth=\"240px\"",page);
        Assert.Contains("class=\"history-detail-preview\">@DetailSummary(row)",page);
        Assert.Contains("Click=\"@(()=>_detailRow=row)\"",page);
        Assert.Contains("aria-labelledby=\"history-detail-title\"",page);
        Assert.Contains("class=\"history-detail-text\">@DetailSummary(detail)",page);
        Assert.Contains("Click=\"CloseDetail\"",page);
        Assert.Contains("CloseLots();CloseDetail();_children.Clear()",page);
        Assert.Contains("text-overflow:ellipsis",css);
        Assert.Contains("white-space:pre-wrap;overflow-wrap:anywhere",css);
    }

    [SkippableFact]
    public void Actual_history_groups_DCRs_without_losing_shipped_quantities_or_LOT_details()
    {
        var connection=Environment.GetEnvironmentVariable("AMES_SHIPPING_READ_TEST_DB");
        Skip.If(string.IsNullOrWhiteSpace(connection),"SELECT-only FG history verification.");
        var factory=new AmesConnectionFactory(connection!);
        var repo=new FinishedGoodsRepository(factory);
        var rows=repo.ListHistory();
        Assert.Equal(rows.Count,rows.Select(row=>row.RowKey).Distinct().Count());
        Assert.Equal(rows.Count,rows.Distinct().Count());
        Assert.All(rows,row=>Assert.False(string.IsNullOrWhiteSpace(row.RowKey)));

        var shipments=repo.ListHistory(eventType:"SHIPPED");
        Assert.All(shipments,row=>Assert.True(row.HasShipmentDetails));
        using var conn=factory.OpenConnection();
        using var cmd=new SqlCommand("""
            SELECT TOP(1000)
                CASE WHEN D.DeliveryID IS NOT NULL THEN CONCAT('DCR:',D.DeliveryID)
                     ELSE CONCAT('SHIP:',S.ShipmentID) END RowKey,
                SUM(-T.QtyChange) Qty,COUNT(*) ItemCount
            FROM dbo.FG_Shipment S JOIN dbo.WH_InventoryTransaction T
                ON T.SourceType='FG_OUTBOUND' AND T.SourceID=S.ShipmentID AND T.TransactionType='OUT'
            LEFT JOIN (SELECT DISTINCT DeliveryID,ShipmentID FROM dbo.FG_DeliveryLoad WHERE ShipmentID IS NOT NULL) L
                ON L.ShipmentID=S.ShipmentID
            LEFT JOIN dbo.FG_Delivery D ON D.DeliveryID=L.DeliveryID
            GROUP BY D.DeliveryID,CASE WHEN D.DeliveryID IS NULL THEN S.ShipmentID END,
                CASE WHEN D.DeliveryID IS NOT NULL THEN CONCAT('DCR:',D.DeliveryID)
                     ELSE CONCAT('SHIP:',S.ShipmentID) END
            ORDER BY MAX(S.ShippedAt) DESC;
            """,conn);
        using var reader=cmd.ExecuteReader();
        var expected=new Dictionary<string,(decimal Qty,int ItemCount)>();
        while(reader.Read())expected.Add(reader.GetString(0),(reader.GetDecimal(1),reader.GetInt32(2)));
        Assert.Equal(expected.Count,shipments.Count);
        foreach(var shipment in shipments)
        {
            Assert.Equal(expected[shipment.RowKey].Qty,shipment.Qty);
            Assert.Equal(expected[shipment.RowKey].ItemCount,shipment.ItemCount);
            var parts=repo.ListShipmentHistoryParts(shipment.DeliveryId,shipment.ShipmentId!.Value);
            Assert.Equal(shipment.Qty,parts.Sum(part=>part.Qty));
            var lots=parts.SelectMany(part=>repo.ListShipmentHistoryLots(shipment.DeliveryId,shipment.ShipmentId.Value,part.PalletNo,part.PartNo)).ToList();
            Assert.Equal(shipment.ItemCount,lots.Count);
            Assert.Equal(lots.Count,lots.Select(lot=>lot.TransactionId).Distinct().Count());
            if(shipment.DeliveryId.HasValue)
            {
                Assert.StartsWith("DCR:",shipment.RowKey);
                Assert.Equal(shipment,Assert.Single(repo.ListHistory(shipment.ReferenceNo,"SHIPPED"),row=>row.RowKey==shipment.RowKey));
                foreach(var term in new[]{parts.FirstOrDefault()?.PalletNo,parts.FirstOrDefault()?.PartNo,lots.FirstOrDefault()?.LotNo}.Where(term=>!string.IsNullOrWhiteSpace(term)))
                    Assert.Equal(shipment.Qty,Assert.Single(repo.ListHistory(term,"SHIPPED"),row=>row.RowKey==shipment.RowKey).Qty);
            }
            else Assert.StartsWith("SHIP:",shipment.RowKey);
        }
        var reload=repo.ListHistory();
        Assert.Equal(rows.Select(row=>row.RowKey),reload.Select(row=>row.RowKey));
    }

    private static string Root([System.Runtime.CompilerServices.CallerFilePath]string source="")
        =>Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!,"../../.."));
}
