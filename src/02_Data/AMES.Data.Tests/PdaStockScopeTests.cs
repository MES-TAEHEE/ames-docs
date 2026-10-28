using AMES.Data.Connection;
using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;
using System.Data;
using Xunit;

namespace AMES.Data.Tests;

public sealed class PdaStockScopeTests
{
    [Theory]
    [InlineData("FG", "EXISTS")]
    [InlineData("GENERAL", "NOT EXISTS")]
    public void Inventory_scope_uses_item_type_and_excludes_container_totals(string kind, string predicate)
    {
        foreach (var sql in new[] { PdaWarehouseSql.Inventory, PdaWarehouseSql.InventoryLocations })
        {
            var scoped = PdaStockScope.Inventory(sql, kind);
            Assert.DoesNotContain("LIKE 'FG%'", scoped);
            Assert.DoesNotContain("='FG_AREA'", scoped);
            Assert.Contains(predicate+"(SELECT 1 FROM dbo.MD_Item SI", scoped);
            Assert.Contains("SI.ItemType='ASSY'", scoped);
            Assert.Contains("Child.ParentLotNo=W.LotNo", scoped);
        }
    }

    [Fact]
    public void All_stock_keeps_unknown_items_and_transactions_do_not_depend_on_current_stock()
    {
        Assert.Equal("1=1", PdaStockScope.Predicate("ALL"));
        var sql = PdaStockScope.Transactions("FG");
        Assert.Contains("SI.ItemType='ASSY'", sql);
        Assert.Contains("T.PartNo COLLATE DATABASE_DEFAULT", sql);
        Assert.Contains("T.TransactionTime >= @From", sql);
        Assert.DoesNotContain("W.Qty>0", sql);
        Assert.False(PdaStockScope.IsValid("FG';DELETE"));
        Assert.Throws<ArgumentException>(() => PdaStockScope.Predicate("bad"));
    }

    [Fact]
    public void Pda_menu_names_and_shared_filters_are_consistent()
    {
        var fg=Read("src/05_Pda/AMES.Pda/Components/Pages/Fg/FgHome.razor");
        Assert.Contains(">ASSY Ready</div>",fg);
        Assert.DoesNotContain(">Set Location</div>",fg);
        foreach(var label in new[]{">Inventory</div>",">Adjust</div>",">TRANSACTIONS</div>"}) Assert.DoesNotContain(label,fg);
        Assert.Contains(">Set Location</div>",Read("src/05_Pda/AMES.Pda/Components/Pages/Wh/WhHome.razor"));
        var inventory=Read("src/05_Pda/AMES.Pda/Components/Pages/Wh/Wh03InventoryStatus.razor");
        var history=Read("src/05_Pda/AMES.Pda/Components/Pages/Wh/Wh08TransactionHistory.razor");
        Assert.Contains("<PdaStockFilter Value=\"@_stockKind\"",inventory);
        Assert.Contains("@if (!IsSpareParts && !IsAdjustTab)",inventory);
        Assert.Contains("autoDetect:!IsSpareParts",inventory);
        Assert.Contains("_detectedFinishedGoods = string.Equals(row.ReceiveType,\"FG\"",inventory);
        Assert.DoesNotContain("_stockKind = \"GENERAL\"",inventory);
        Assert.DoesNotContain("<PdaStockFilter",history);
        Assert.Contains("Api.TransactionPageAsync",history);
        Assert.Contains("if (IsAdjustTab && Auth.Session?.IsAdmin != true)",inventory);
        Assert.Contains("ClearInventoryWork();",inventory);
        Assert.Contains("FG (ASSY)",Read("src/05_Pda/AMES.Pda/Components/PdaStockFilter.razor"));
    }

    [SkippableFact]
    public void Live_inventory_scopes_partition_the_same_stock_without_writes()
    {
        var connection=Environment.GetEnvironmentVariable("AMES_TEST_CONN");
        Skip.If(string.IsNullOrWhiteSpace(connection),"Set AMES_TEST_CONN for read-only stock query verification.");
        using var conn=new AmesConnectionFactory(connection!).OpenConnection();
        var counts=new Dictionary<string,int>();
        foreach(var kind in new[]{"ALL","GENERAL","FG"})
        {
            using var cmd=new SqlCommand(PdaStockScope.Inventory(PdaWarehouseSql.Inventory,kind),conn);
            cmd.Parameters.Add("@SearchText",SqlDbType.NVarChar,80).Value=DBNull.Value;
            cmd.Parameters.Add("@StockDateFrom",SqlDbType.Date).Value=DBNull.Value;
            cmd.Parameters.Add("@StockDateTo",SqlDbType.Date).Value=DBNull.Value;
            cmd.Parameters.Add("@AreaCode",SqlDbType.NVarChar,20).Value=DBNull.Value;
            using var reader=cmd.ExecuteReader();
            var count=0;while(reader.Read())count++;
            counts[kind]=count;
        }
        Assert.Equal(counts["ALL"],counts["GENERAL"]+counts["FG"]);
    }

    [Fact]
    public void By_part_keeps_production_stock_waiting_for_a_location()
    {
        var api=Read("src/04_Api/AMES.Api/Endpoints/WhEndpoints.cs");
        var partLots=api.Split("g.MapGet(\"/inventory/part/{itemNo}/lots\"")[1].Split("g.MapGet(\"/inventory/location-list\"")[0];
        Assert.DoesNotContain("AND NULLIF(LTRIM(RTRIM(W.LocationNo)), '') IS NOT NULL",partLots);
        Assert.Contains("\"RECEIVED\" : \"STORED\"",partLots);
    }

    private static string Read(string path,[System.Runtime.CompilerServices.CallerFilePath]string source="")
        => File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!,"../../..",path)));
}
