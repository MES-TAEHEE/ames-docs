using AMES.Data.Repositories;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// 10-20 — MD-019 단위 관리는 비활성 포장 단위(BOX·PLT·SH)까지 전부 보이고, 선택 콤보용 기본 목록은 포장 단위를 뺀다
public class UomListTests
{
    [SkippableFact]
    public void Retired_packaging_units_are_listed_only_when_asked()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var md = new MasterDataRepository(f!);

        var all = md.ListUoms(includeRetired: true);
        Assert.Equal(Convert.ToInt32(Scalar(f!, "SELECT COUNT(*) FROM dbo.MD_Uom")), all.Count);

        var selectable = md.ListUoms();
        Assert.All(selectable, u => Assert.True(MasterDataRepository.IsQuantityUnit(u.UOMCode)));
        Assert.Equal(all.Count(u => MasterDataRepository.IsQuantityUnit(u.UOMCode)), selectable.Count);
    }
}
