using AMES.Data.Repositories;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// MD-18-U — BOP 단계(MD_Bop.InspStdID)가 QC 검사 기준으로 쓰는 검사 기준은 삭제되지 않고 사용처가 돌아온다
public class InspectionStandardDeleteTests
{
    const string WriteSkip = "set AMES_TEST_CONN (local DB) to run — creates and removes a test inspection standard and BOP step";
    const string Code = "ITEST-IS-01";
    const string Bop  = "ITEST-IS-BOP";

    static void Cleanup(Connection.AmesConnectionFactory f) => Exec(f, """
        DELETE dbo.MD_Bop WHERE BOPID = @B;
        DELETE dbo.MD_InspectionStandard WHERE InspStdID = @C;
        """, ("@C", Code), ("@B", Bop));

    [SkippableFact]
    public void Standard_used_by_a_bop_step_is_not_deleted_until_released()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = TryFactory(); Skip.If(f is null, WriteSkip);
        var md = new MasterDataRepository(f!);
        Cleanup(f!);
        try
        {
            md.InsertInspectionStandard(Code, null, null, null, "dim", 10m, 9m, 11m, null, null, null, false, null, true, "itest");
            Assert.Empty(md.InspectionStandardUsages(Code));

            Exec(f!, "INSERT dbo.MD_Bop (BOPID, ItemNo, RoutingType, StepSeq, QcRequiredFlag, InspStdID, CreatedBy) VALUES (@B, 'ITEST-IS-ITEM', 'A', 10, 1, @C, 'itest')", ("@B", Bop), ("@C", Code));
            var used = md.DeleteInspectionStandard(Code);
            Assert.Contains(used, u => u.Kind == "BOP");
            Assert.NotNull(Scalar(f!, "SELECT 1 FROM dbo.MD_InspectionStandard WHERE InspStdID = @C", ("@C", Code)));

            // 비활성으로 바꿔도 사용 중이면 지워지지 않는다
            Exec(f!, "UPDATE dbo.MD_InspectionStandard SET ActiveFlag = 0 WHERE InspStdID = @C", ("@C", Code));
            Assert.NotEmpty(md.DeleteInspectionStandard(Code));

            // BOP 단계의 연결을 풀면 삭제된다
            Exec(f!, "UPDATE dbo.MD_Bop SET InspStdID = NULL WHERE BOPID = @B", ("@B", Bop));
            Assert.Empty(md.DeleteInspectionStandard(Code));
            Assert.Null(Scalar(f!, "SELECT 1 FROM dbo.MD_InspectionStandard WHERE InspStdID = @C", ("@C", Code)));
        }
        finally { Cleanup(f!); }
    }

    [Theory]
    [InlineData(true, " X-1 ", "X-1")]
    [InlineData(true, "", null)]
    [InlineData(true, null, null)]
    [InlineData(false, "X-1", null)]
    public void Bop_inspection_standard_is_kept_only_for_qc_required_steps(bool qc, string? id, string? expected)
        => Assert.Equal(expected, MasterDataRepository.NormalizeBopInspStd(qc, id));
}
