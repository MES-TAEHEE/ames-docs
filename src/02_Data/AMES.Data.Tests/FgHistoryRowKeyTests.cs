using AMES.Data.Connection;
using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.Data.Tests;

public sealed class FgHistoryRowKeyTests
{
    [Fact]
    public void Identical_visible_shipment_lines_still_have_distinct_record_and_grid_keys()
    {
        var first=new FinishedGoodsRepository.HistoryRow("TX:1",new(2026,10,28),"SHIPPED","170","PART",1,"LOC","USER","SHIPPED","PALLET");
        var second=first with { RowKey="TX:2" };
        Assert.NotEqual(first,second);
        Assert.NotEqual(first.RowKey,second.RowKey);
        var page=File.ReadAllText(Path.Combine(Root(),"src/06_Web/AMES.Web/Components/Pages/Fg/History.razor"));
        Assert.Contains("KeyProperty=\"RowKey\"",page);
    }

    [SkippableFact]
    public void Actual_history_preserves_all_shipped_transactions_without_duplicate_keys()
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
        Assert.All(shipments,row=>Assert.StartsWith("TX:",row.RowKey));
        using var conn=factory.OpenConnection();
        using var cmd=new SqlCommand("""
            SELECT TOP(1000) T.TransactionID
            FROM dbo.FG_Shipment S JOIN dbo.WH_InventoryTransaction T
                ON T.SourceType='FG_OUTBOUND' AND T.SourceID=S.ShipmentID AND T.TransactionType='OUT'
            ORDER BY S.ShippedAt DESC,CONVERT(nvarchar(80),S.ShipmentID) DESC,
                CONCAT('TX:',CONVERT(varchar(20),T.TransactionID)) DESC;
            """,conn);
        using var reader=cmd.ExecuteReader();
        var expected=new List<string>();
        while(reader.Read())expected.Add($"TX:{Convert.ToInt64(reader[0])}");
        Assert.Equal(expected,shipments.Select(row=>row.RowKey));
        var reload=repo.ListHistory();
        Assert.Equal(rows.Select(row=>row.RowKey),reload.Select(row=>row.RowKey));
    }

    private static string Root([System.Runtime.CompilerServices.CallerFilePath]string source="")
        =>Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!,"../../.."));
}
