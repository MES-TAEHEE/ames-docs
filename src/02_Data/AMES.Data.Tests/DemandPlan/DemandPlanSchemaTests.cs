using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests.DemandPlan;

/// <summary>migrate_demand_plan.sql 적용 여부 — 테이블·컬럼·공통코드. AMES_DEV 통합, DB 미기동 시 skip.</summary>
public class DemandPlanSchemaTests
{
    [SkippableFact]
    public void Migration_objects_exist()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Assert.NotNull(Scalar(f!, "SELECT OBJECT_ID('dbo.PP_DemandPlan', 'U')"));
        Assert.NotNull(Scalar(f!, "SELECT OBJECT_ID('dbo.PP_DemandPlanBatch', 'U')"));
        Assert.NotNull(Scalar(f!, "SELECT COL_LENGTH('dbo.PP_ApsRun', 'IncludeDailyPlan')"));
        Assert.Equal(1, Scalar(f!, "SELECT COUNT(*) FROM dbo.MD_CodeItem WHERE GroupCode = 'SW_DPSYNC' AND CodeValue = 'INTERVAL' AND Attribute1 = '60'"));
        Assert.Equal(4, Scalar(f!, "SELECT COUNT(*) FROM dbo.MD_CodeGroup WHERE GroupCode LIKE 'SW[_]DPSYNC%'"));
        Assert.Equal(1, Scalar(f!, "SELECT COUNT(*) FROM sys.indexes WHERE name = 'UX_PP_DemandPlan_Cust_Item_Date' AND is_unique = 1"));
    }
}
