using System.Text.RegularExpressions;
using AMES.Data.Repositories;
using AMES.Data.Services;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.Data.Tests;

public sealed class WarehouseReviewRegressionTests
{
    [Fact]
    public void Putaway_includes_repaired_goods_but_still_requires_assy_and_preserves_existing_quantity()
    {
        Assert.Contains("OR P.ProcessCode='RWK'",PdaFinishedGoodsSql.PutAway);
        Assert.Contains("I.ItemType='ASSY'",PdaFinishedGoodsSql.PutAway);
        Assert.Contains("SET @Qty=COALESCE(@StoredQty,@ProductionQty)",PdaFinishedGoodsSql.PutAway);
        var api=Read("src/04_Api/AMES.Api/Endpoints/FgEndpoints.cs");
        Assert.DoesNotContain("gsLot[12..]",api);
        Assert.Contains("NULLIF(LTRIM(RTRIM(S.LocationNo)),'') IS NOT NULL",api);
    }

    [Fact]
    public void Unlocated_stock_remains_visible_without_counting_container_totals_twice()
    {
        var source=Read("src/02_Data/AMES.Data/Repositories/WarehouseRepository.cs");
        source=source[source.IndexOf("public List<LocationInventoryRow> ListLocationInventory",StringComparison.Ordinal)..];
        Assert.Contains("LEFT JOIN dbo.MD_Location L ON L.LocationID=S.LocationNo",source);
        Assert.Contains("C.ParentLotNo=S.LotNo",source);
        var report=Read("src/02_Data/AMES.Data/Repositories/RptRepository.cs");
        Assert.Contains("OR NULLIF(LTRIM(RTRIM(w.LocationNo)),'') IS NULL",report);
        Assert.Contains("C.ParentLotNo=w.LotNo",report);
    }

    private static string Read(string path,[System.Runtime.CompilerServices.CallerFilePath] string file="")
        =>File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,"../../..",path)));
}
