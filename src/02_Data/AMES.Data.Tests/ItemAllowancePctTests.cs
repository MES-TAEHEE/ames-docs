using AMES.Data.Repositories;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// 10-20 — MD_Item.IssueAddPct(추가 불출율)·MrpAllowPct(MRP 여유율)가 MD-003 저장 경로로 왕복하고,
// 0 은 NULL 로 저장하며 null 수정은 저장된 값을 두는지 + 컬럼이 LeadTimeDays 와 ProcureType 사이인지
public class ItemAllowancePctTests
{
    const string WriteSkip = "set AMES_TEST_CONN (local DB) to run — creates and removes a test item";
    const string Actor = "itest-allow";
    const string Item  = "ITEST-ALLOW-ITEM";

    static void Cleanup(Connection.AmesConnectionFactory f) =>
        Exec(f, "DELETE dbo.MD_Item WHERE ItemNo = @I", ("@I", Item));

    static MasterDataRepository.ItemRow Row(MasterDataRepository md) => Assert.Single(md.ListItems(Item));

    static void Update(MasterDataRepository md, decimal? issue, decimal? mrp) =>
        md.UpdateItem(Item, "itest allow", "MATERIAL", null, false, null, "EA", null, null, null, null, null, null, null, null,
            null, null, false, true, Actor, issueAddPct: issue, mrpAllowPct: mrp);

    [Theory]
    [InlineData(null, null)]
    [InlineData(0, null)]
    [InlineData(10, 10)]
    [InlineData(100, 100)]
    public void Normalize_keeps_0_to_100_and_stores_zero_as_null(int? input, int? expected) =>
        Assert.Equal((decimal?)expected, MasterDataRepository.NormalizeAllowancePct(input, "pct"));

    [Theory]
    [InlineData(-1)]
    [InlineData(100.01)]
    public void Normalize_rejects_out_of_range(double input) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => MasterDataRepository.NormalizeAllowancePct((decimal)input, "pct"));

    [SkippableFact]
    public void Columns_sit_between_lead_time_and_procure_type()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var cols = Scalar(f!, """
            SELECT STRING_AGG(CAST(c.name AS nvarchar(max)), ',') WITHIN GROUP (ORDER BY c.column_id)
            FROM sys.columns c
            WHERE c.object_id = OBJECT_ID(N'dbo.MD_Item')
              AND c.column_id BETWEEN COLUMNPROPERTY(OBJECT_ID(N'dbo.MD_Item'), N'LeadTimeDays', 'ColumnId')
                                  AND COLUMNPROPERTY(OBJECT_ID(N'dbo.MD_Item'), N'ProcureType', 'ColumnId')
            """) as string;
        Assert.Equal("LeadTimeDays,IssueAddPct,MrpAllowPct,ProcureType", cols);
    }

    [SkippableFact]
    public void Percentages_roundtrip_and_a_null_update_keeps_the_stored_value()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = TryFactory(); Skip.If(f is null, WriteSkip);
        var md = new MasterDataRepository(f!);
        try
        {
            Cleanup(f!);
            md.InsertItem(Item, "itest allow", "MATERIAL", null, false, null, "EA", null, null, null, null, null, null, null, null,
                null, null, false, true, Actor, issueAddPct: 10m, mrpAllowPct: 5.5m);
            Assert.Equal((10m, 5.5m), (Row(md).IssueAddPct, Row(md).MrpAllowPct));

            Update(md, null, null);                       // 값을 넘기지 않으면 그대로
            Assert.Equal((10m, 5.5m), (Row(md).IssueAddPct, Row(md).MrpAllowPct));

            Update(md, 12.25m, null);                     // 하나만 바꿔도 다른 하나는 그대로
            Assert.Equal((12.25m, 5.5m), (Row(md).IssueAddPct, Row(md).MrpAllowPct));

            Update(md, 0, 0);                             // 0 = 비움(NULL)
            Assert.Equal(((decimal?)null, (decimal?)null), (Row(md).IssueAddPct, Row(md).MrpAllowPct));
            Assert.Equal(DBNull.Value, Scalar(f!, "SELECT IssueAddPct FROM dbo.MD_Item WHERE ItemNo = @I", ("@I", Item)) ?? DBNull.Value);

            Assert.Throws<ArgumentOutOfRangeException>(() => Update(md, 101m, null));
        }
        finally { Cleanup(f!); }
    }
}
