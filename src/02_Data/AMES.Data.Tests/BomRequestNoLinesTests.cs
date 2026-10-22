using AMES.Data.Repositories;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// 10-20 — 활성 구성요소(BOM 라인)가 없는 DRAFT 버전은 승인 요청할 수 없다(비활성 라인만 있어도 거부)
public class BomRequestNoLinesTests
{
    const string WriteSkip = "set AMES_TEST_CONN (local DB) to run — creates and removes a test item, BOM version and line";
    const string Actor = "itest-bomreq";
    const string Item  = "ITEST-BOMREQ";
    const string Ver   = "ITEST-BOMREQ-V";
    const string Line  = "ITEST-BOMREQ-L";

    static void Cleanup(Connection.AmesConnectionFactory f) => Exec(f, """
        DELETE dbo.MD_Bom        WHERE VersionID = @V;
        DELETE dbo.MD_BomVersion WHERE VersionID = @V;
        DELETE dbo.MD_Item       WHERE ItemNo = @I;
        """, ("@V", Ver), ("@I", Item));

    static string? Status(Connection.AmesConnectionFactory f) =>
        Scalar(f, "SELECT Status FROM dbo.MD_BomVersion WHERE VersionID = @V", ("@V", Ver)) as string;

    [SkippableFact]
    public void Approval_request_needs_an_active_component_line()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = TryFactory(); Skip.If(f is null, WriteSkip);
        var md = new MasterDataRepository(f!);
        try
        {
            Cleanup(f!);
            md.InsertItem(Item, "itest bom request", "ASSY", null, false, null, "EA", null, null, null, null, null, null, null, null,
                null, null, false, true, Actor);
            md.InsertBomVersion(Ver, Item, "V1.0", new DateOnly(2099, 1, 1), null, null, null, Actor);

            var ex = Assert.Throws<MasterDataRepository.BomHasNoLinesException>(() => md.RequestBomApproval(Ver, Actor));
            Assert.Equal(Ver, ex.VersionId);
            Assert.Equal("DRAFT", Status(f!));

            Assert.True(md.InsertBomLine(Line, Item, Item, 1, 1m, null, Ver, 10, null, Actor));
            Assert.True(md.DeleteBomLine(Line, Actor));   // 비활성 라인만 남음
            Assert.Throws<MasterDataRepository.BomHasNoLinesException>(() => md.RequestBomApproval(Ver, Actor));
            Assert.Equal("DRAFT", Status(f!));

            Assert.True(md.ReactivateBomLine(Line, Actor));
            Assert.True(md.RequestBomApproval(Ver, Actor));
            Assert.Equal("PENDING", Status(f!));

            Assert.False(md.RequestBomApproval(Ver, Actor));   // 이미 PENDING — 상태 변경 없음
        }
        finally { Cleanup(f!); }
    }
}
