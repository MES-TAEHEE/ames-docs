using AMES.Contracts.Formatting;
using AMES.Data.Repositories;
using Xunit;

namespace AMES.Data.Tests;

public sealed class StockCountQuantityTests
{
    [Theory]
    [InlineData("1.125", "G", true)]
    [InlineData("0.005", "SH", true)]
    [InlineData("1.125", "EA", false)]
    [InlineData("1.000", "ea", true)]
    [InlineData("0", "G", true)]
    [InlineData("-1", "G", false)]
    [InlineData("-0.005", "SH", false)]
    [InlineData("1.0001", "G", false)]
    [InlineData("999999999", "SH", true)]
    [InlineData("999999999.001", "SH", false)]
    [InlineData("1,000", "G", false)]
    [InlineData("1e3", "G", false)]
    [InlineData("1.5", null, false)]
    public void Counts_accept_stored_decimals_but_never_negative_stock(string text,string? unit,bool valid)
        =>Assert.Equal(valid,StockCountQuantity.TryParse(text,unit,out _));

    [Fact]
    public void SQL_checks_master_unit_and_nonnegative_result_before_updating()
    {
        var sql=PdaWarehouseSql.AdjustSave;
        var update=sql.IndexOf("UPDATE dbo.WH_Inventory",StringComparison.Ordinal);
        Assert.InRange(sql.IndexOf("IF @AfterQty < 0",StringComparison.Ordinal),0,update);
        Assert.InRange(sql.IndexOf("EA stock requires a whole-number quantity.",StringComparison.Ordinal),0,update);
        Assert.Contains("UPPER(LTRIM(RTRIM(DefaultUOM)))<>'EA'",sql);
        Assert.False(StockCountQuantity.HasStoragePrecision(0.0001m));
        Assert.True(StockCountQuantity.HasStoragePrecision(-0.125m));
    }

    [Fact]
    public void PDA_count_keeps_fractional_stock_and_limits_decimal_pad()
    {
        var page=File.ReadAllText(Path.Combine(Root(),"src/05_Pda/AMES.Pda/Components/Pages/Wh/Wh03InventoryStatus.razor"));
        Assert.Contains("StockCountQuantity.TryParse",page);
        Assert.Contains("StockCountQuantity.IsValid",page);
        Assert.Contains("aria-label=\"Decimal point\"",page);
        Assert.DoesNotContain("decimal.Truncate(InventoryAfterQty)",page);
        Assert.DoesNotContain("{value:N0}",page);
        Assert.Contains("Math.Max(0,value):#,##0.###",page);
        Assert.Contains("IsSpareParts || IsFinishedGoodsAdjust ? \"EA\"",page);
    }

    private static string Root([System.Runtime.CompilerServices.CallerFilePath]string file="")
        =>Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,"../../.."));
}
