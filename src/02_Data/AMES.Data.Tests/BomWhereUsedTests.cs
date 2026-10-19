using AMES.Data.Repositories;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// 10-20 — MD-004 상위 BOM 역전개: 구성품으로 찾은 활성 라인과 버전으로 찾은 라인이 같은 행이다(읽기 전용)
public class BomWhereUsedTests
{
    [SkippableFact]
    public void Where_used_finds_the_same_active_line_as_the_version_listing()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var id = Scalar(f!, "SELECT TOP 1 BOMID FROM dbo.MD_Bom WHERE ISNULL(ActiveFlag,1) = 1 AND CompItemNo IS NOT NULL ORDER BY BOMID") as string;
        Skip.If(id is null, "no active BOM line");
        var comp = (string)Scalar(f!, "SELECT CompItemNo FROM dbo.MD_Bom WHERE BOMID = @B", ("@B", id!))!;
        var ver  = (string)Scalar(f!, "SELECT VersionID  FROM dbo.MD_Bom WHERE BOMID = @B", ("@B", id!))!;
        var md = new MasterDataRepository(f!);

        var used = md.ListBomWhereUsed(comp);
        Assert.Contains(used, r => r.BOMID == id);
        Assert.All(used, r => Assert.True(r.ActiveFlag && string.Equals(r.CompItemNo, comp, StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(used.Single(r => r.BOMID == id), md.ListBomLines(ver).Single(r => r.BOMID == id));
    }
}
