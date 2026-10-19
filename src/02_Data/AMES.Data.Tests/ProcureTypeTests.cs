using AMES.Data.Repositories;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// 10-20 — MD_Item.ProcureType(조달형태, 공통코드 PROCURE_TYPE)가 MD-003 저장 경로로 왕복하고,
// 값을 넘기지 않은 수정(null)은 저장된 값을 두며 빈 문자열은 지우는지 + 컬럼이 SAPTransFlag 바로 앞인지
public class ProcureTypeTests
{
    const string WriteSkip = "set AMES_TEST_CONN (local DB) to run — creates and removes a test item";
    const string Actor = "itest-proc";
    const string Item  = "ITEST-PROC-ITEM";

    static void Cleanup(Connection.AmesConnectionFactory f) =>
        Exec(f, "DELETE dbo.MD_Item WHERE ItemNo IN (@I, @I2)", ("@I", Item), ("@I2", Item + "2"));

    static string? Stored(Connection.AmesConnectionFactory f) =>
        Scalar(f, "SELECT ProcureType FROM dbo.MD_Item WHERE ItemNo = @I", ("@I", Item)) as string;

    void Update(MasterDataRepository md, string? procureType) =>
        md.UpdateItem(Item, "itest proc 2", "MATERIAL", null, false, null, "EA", null, null, null, null, null, null, null, null,
            null, null, false, true, Actor, procureType: procureType);

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("  ", null)]
    [InlineData(" purchase ", "PURCHASE")]
    [InlineData("FREE_SUP", "FREE_SUP")]
    public void Normalize_trims_upper_cases_and_empties_to_null(string? input, string? expected) =>
        Assert.Equal(expected, MasterDataRepository.NormalizeProcureType(input));

    [SkippableFact]
    public void Code_group_has_the_four_procurement_types_and_the_column_sits_before_sap_flag()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var codes = new MasterDataRepository(f!).ListCodeItems(MasterDataRepository.ProcureTypeGroup)
            .Select(c => c.CodeValue).OrderBy(v => v).ToList();
        Assert.Equal(new[] { "FREE_SUP", "INHOUSE", "PAID_SUP", "PURCHASE" }, codes);

        var next = Scalar(f!, """
            SELECT TOP (1) c.name FROM sys.columns c
            WHERE c.object_id = OBJECT_ID(N'dbo.MD_Item')
              AND c.column_id > COLUMNPROPERTY(OBJECT_ID(N'dbo.MD_Item'), N'ProcureType', 'ColumnId')
            ORDER BY c.column_id
            """) as string;
        Assert.Equal("SAPTransFlag", next);
    }

    [SkippableFact]
    public void Procure_type_roundtrips_and_a_null_update_keeps_the_stored_value()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = TryFactory(); Skip.If(f is null, WriteSkip);
        var md = new MasterDataRepository(f!);
        try
        {
            Cleanup(f!);

            md.InsertItem(Item, "itest proc", "MATERIAL", null, false, null, "EA", null, null, null, null, null, null, null, null,
                null, null, false, true, Actor, procureType: " paid_sup ");
            Assert.Equal("PAID_SUP", Assert.Single(md.ListItems(Item)).ProcureType);

            Update(md, null);
            Assert.Equal("PAID_SUP", Stored(f!));

            Update(md, "INHOUSE");
            Assert.Equal("INHOUSE", Assert.Single(md.ListItems(Item)).ProcureType);

            Update(md, "");
            Assert.Null(Stored(f!));
            Assert.Null(Assert.Single(md.ListItems(Item)).ProcureType);

            md.InsertItem(Item + "2", "itest proc", "MATERIAL", null, false, null, "EA", null, null, null, null, null, null, null, null,
                null, null, false, true, Actor);
            Assert.Null(Assert.Single(md.ListItems(Item + "2")).ProcureType);
        }
        finally { Cleanup(f!); }
    }
}
