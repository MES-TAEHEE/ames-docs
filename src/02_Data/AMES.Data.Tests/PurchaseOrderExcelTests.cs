using AMES.Data.Services;
using Xunit;

namespace AMES.Data.Tests;

public class PurchaseOrderExcelTests
{
    [Fact]
    public void Import_uses_reordered_headers_master_codes_and_decimal_quantities()
    {
        var result=PurchaseOrderExcel.Parse([new[]{"Quantity","PartNo"},new[]{"0.125"," resin-1 "},new[]{"2","PART-2"}], ["RESIN-1","PART-2"]);
        Assert.Empty(result.Problems);
        Assert.Equal(new[]{new PurchaseOrderExcel.Line("RESIN-1",0.125m),new PurchaseOrderExcel.Line("PART-2",2m)},result.Lines);
    }
    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.0001")]
    [InlineData("1000000000")]
    [InlineData("=1+1")]
    public void Invalid_quantities_reject_the_entire_file(string invalid)
    {
        var result=PurchaseOrderExcel.Parse([new[]{"PartNo","Quantity"},new[]{"P1","5"},new[]{"P2",invalid}], ["P1","P2"]);
        Assert.Empty(result.Lines);
        Assert.Equal(3,Assert.Single(result.Problems).Line);
    }
    [Fact]
    public void Unknown_parts_duplicates_missing_headers_and_oversized_files_are_rejected()
    {
        Assert.Empty(PurchaseOrderExcel.Parse([new[]{"PartNo","Quantity"},new[]{"UNKNOWN","1"}],["P1"]).Lines);
        var duplicate=PurchaseOrderExcel.Parse([new[]{"PartNo","Quantity"},new[]{"P1","1"},new[]{"p1","2"}], ["P1"]);
        Assert.Equal(MasterExcel.Codes.Duplicate,Assert.Single(duplicate.Problems).Code);
        Assert.Empty(duplicate.Lines);
        Assert.Equal(MasterExcel.Codes.NoHeader,Assert.Single(PurchaseOrderExcel.Parse([new[]{"PartNo"}],[]).Problems).Code);
        Assert.Empty(PurchaseOrderExcel.Parse(Enumerable.Repeat(new[]{"PartNo","Quantity"},1002).ToArray(),[]).Lines);
    }
}
