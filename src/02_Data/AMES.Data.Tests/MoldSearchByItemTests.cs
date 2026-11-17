using AMES.Data.Repositories;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// MD-007 금형별 제품 관리 — 금형 목록을 품번·품명으로도 찾는다(읽기 전용)
public class MoldSearchByItemTests
{
    [SkippableFact]
    public void Item_number_finds_the_molds_that_have_it_as_a_product()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, "set AMES_TEST_CONN to run");
        var f = TryFactory(); Skip.If(f is null, "no dev db");
        var md = new MasterDataRepository(f!);
        var pick = Scalar(f!, "SELECT TOP 1 mi.ItemNo + '|' + mi.MoldID FROM dbo.MD_MoldItem mi ORDER BY mi.MoldID") as string;
        Skip.If(pick is null, "no mold items");
        var (item, mold) = (pick!.Split('|')[0], pick.Split('|')[1]);

        Assert.Contains(mold, md.FindMoldIdsByItem(item));
        Assert.Contains(mold, md.FindMoldIdsByItem(item.ToLowerInvariant()));          // 대소문자 무관
        Assert.Empty(md.FindMoldIdsByItem("NO-SUCH-ITEM-ZZZ"));
        Assert.Empty(md.FindMoldIdsByItem("  "));
    }
}
