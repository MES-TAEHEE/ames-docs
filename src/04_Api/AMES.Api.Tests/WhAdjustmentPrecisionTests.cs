using System.Reflection;
using AMES.Api.Endpoints;
using AMES.Data.Connection;
using Xunit;

namespace AMES.Api.Tests;

public sealed class WhAdjustmentPrecisionTests
{
    [Fact]
    public void Overprecision_is_rejected_before_opening_a_database_or_rounding_the_parameter()
    {
        var save=typeof(WhEndpoints).GetMethod("ExecuteAdjustSave",BindingFlags.NonPublic|BindingFlags.Static)!;
        var request=new WhEndpoints.AdjustSaveReq("WH","LOT",0.0001m,"COUNT_DIFF",null);
        var result=(WhEndpoints.InboundReceiveResult)save.Invoke(null,[new AmesConnectionFactory("invalid"),request,"TEST",false])!;
        Assert.False(result.Success);
        Assert.Contains("3 decimal places",result.Message);
    }

    [ReadOnlyDatabaseFact]
    public void Auto_scan_identifies_actual_ASSY_and_gram_stock_without_any_writes()
    {
        var connection=Environment.GetEnvironmentVariable("AMES_SHIPPING_READ_TEST_DB");
        var factory=new AmesConnectionFactory(connection!);
        var scan=typeof(WhEndpoints).GetMethod("ExecuteAdjustScan",BindingFlags.NonPublic|BindingFlags.Static)!;
        var gram=(WhEndpoints.InboundScanRow)scan.Invoke(null,[factory,"UIT-LP-DEAHA-20261021-0002",true])!;
        Assert.Equal("G",gram.Unit);
        Assert.Equal("21326B1-PIDHA",gram.PartNo);
        Assert.NotEqual("FG",gram.ReceiveType);
        var assy=(WhEndpoints.InboundScanRow)scan.Invoke(null,[factory,"AAMP10097",true])!;
        Assert.Equal("FG",assy.ReceiveType);
        Assert.Equal("PNTDEMO-A",assy.PartNo);
        Assert.Equal("EA",assy.Unit);
    }

    private sealed class ReadOnlyDatabaseFactAttribute:FactAttribute
    {
        public ReadOnlyDatabaseFactAttribute()
        {
            if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AMES_SHIPPING_READ_TEST_DB")))
                Skip="SELECT-only scan verification.";
        }
    }
}
