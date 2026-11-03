using System.Runtime.CompilerServices;
using AMES.Api.Endpoints;
using AMES.Data.Repositories;
using Xunit;

namespace AMES.Api.Tests;

// Pure policy and source guards only; no SQL connections or inventory writes.
public sealed class PutAwayLocationPolicyTests
{
    [Theory]
    [InlineData("FG", null, true)]
    [InlineData("fg", null, true)]
    [InlineData("FG", "", true)]
    [InlineData(" FG ", " ", true)]
    [InlineData("WH", null, false)]
    [InlineData("WAREHOUSE", null, false)]
    [InlineData("PRODUCTION_LINE", "LINE-1", false)]
    [InlineData("LINE", "LINE-1", false)]
    [InlineData(null, "LINE-1", false)]
    [InlineData(null, null, false)]
    [InlineData("", null, false)]
    [InlineData("FG", "LINE-1", false)]
    [InlineData("FG", " LINE-1 ", false)]
    public void Assy_requires_an_FG_location_that_is_not_linked_to_a_production_line(
        string? locationType, string? lineId, bool allowed) =>
        Assert.Equal(allowed, FgEndpoints.IsFgPutAwayLocation(locationType, lineId));

    [Fact]
    public void FG_scan_and_save_both_reject_warehouse_and_line_locations()
    {
        var api = Read("src/04_Api/AMES.Api/Endpoints/FgEndpoints.cs");
        Assert.Contains("L.LocationType, L.LineID", api);
        Assert.Contains("IsFgPutAwayLocation(GetString(rdr, \"LocationType\"), GetString(rdr, \"LineID\"))", api);
        const string message = "ASSY finished goods require an FG location. Warehouse and production-line locations are not allowed.";
        Assert.Contains(message, api);
        var sql = PdaFinishedGoodsSql.PutAway;
        Assert.Contains("LocationType='FG' AND LocationLevel=2", sql);
        Assert.Contains("AND NULLIF(LTRIM(RTRIM(LineID)),'') IS NULL", sql);
        var guard = sql.IndexOf("THROW 52517", StringComparison.Ordinal);
        Assert.True(guard >= 0);
        Assert.Contains(message, sql);
        Assert.True(guard < sql.IndexOf("INSERT dbo.WH_Inventory(", StringComparison.Ordinal));
        Assert.True(guard < sql.IndexOf("UPDATE dbo.WH_Inventory SET", StringComparison.Ordinal));
        Assert.Contains("FROM dbo.MD_Location WITH(UPDLOCK,HOLDLOCK)", sql);
        Assert.Contains("IF @@TRANCOUNT>0 ROLLBACK TRANSACTION", sql);
    }

    [Fact]
    public void General_stock_cannot_bypass_the_type_check_by_confirming_or_receiving_directly()
    {
        const string message = "Only ASSY finished goods can be assigned an FG location. Scan a warehouse or production-line location for SUB or MATERIAL stock.";
        var move = PdaWarehouseSql.InboundMove;
        Assert.Contains("WHERE ItemNo=@PartNo AND ItemType='ASSY'", move);
        Assert.Contains("WHERE LocationID=@Location AND UPPER(LocationType)='FG'", move);
        Assert.Contains(message, move);
        Assert.True(move.IndexOf(message, StringComparison.Ordinal)
            < move.IndexOf("UPDATE dbo.WH_Inventory SET", StringComparison.Ordinal));
        Assert.Contains("AND NULLIF(LTRIM(RTRIM(LineID)),'') IS NULL", move);
        var receive = PdaWarehouseSql.InboundReceive;
        Assert.Contains(message, receive);
        Assert.True(receive.IndexOf(message, StringComparison.Ordinal)
            < receive.IndexOf("IF @PortalExistingQty IS NULL", StringComparison.Ordinal));
        var page = Read("src/05_Pda/AMES.Pda/Components/Pages/Wh/WhPutAway.razor");
        Assert.Contains(message, page);
        Assert.Contains("ShowAlert(\"Location Scan Failed\", ex.Message)", page);
    }

    [Fact]
    public void Scenario_replaces_unprintable_primary_scan_with_each_item_type_mismatch()
    {
        var wh = Read("docs/test-scenario/scenarios-b.js");
        Assert.Contains("id:'WH-05-N', t:'ASSY의 창고·라인 위치 적재 차단'", wh);
        Assert.Contains("id:'WH-05-N2', t:'SUB의 FG 위치 적재 차단'", wh);
        Assert.Contains("id:'WH-05-N3', t:'MATERIAL의 FG 위치 적재 차단'", wh);
        Assert.Contains("id:'WH-05-N4', t:'검사 대기 반품의 출고 차단 안내'", wh);
        foreach (var file in new[] { "scenarios-b.js", "scenarios-c.js", "scenarios-e.js" })
        {
            var source = Read("docs/test-scenario/" + file);
            Assert.DoesNotContain("대표 위치를 스캔", source);
            Assert.DoesNotContain("상위 로케이션을 찍", source);
            Assert.DoesNotContain("상위는 거부 확인", source);
        }
    }

    private static string Read(string path, [CallerFilePath] string current = "") =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(current)!, "../../..", path)));
}
