using AMES.Data.Repositories;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// WO 흐름 보기의 LOT 칩·LOT 팝업 조회(읽기 전용)
public class LotDetailTests
{
    [SkippableFact]
    public void Work_order_lots_open_into_lot_details()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, "set AMES_TEST_CONN to run");
        var f = TryFactory(); Skip.If(f is null, "no dev db");
        var woId = Scalar(f!, "SELECT TOP 1 l.WoID FROM dbo.tbl_Lot l WHERE l.WoID IS NOT NULL ORDER BY l.LotID DESC") as int?;
        Skip.If(woId is null, "no work order with lots");
        var repo = new WorkOrderRepository(f!);

        var lots = repo.ListWoLots(woId!.Value);
        Assert.NotEmpty(lots);
        var detail = repo.GetLotDetail(lots[0].LotCode);
        Assert.NotNull(detail);
        Assert.Equal(lots[0].LotCode, detail!.LotCode);
        Assert.Null(repo.GetLotDetail("NO-SUCH-LOT-ZZZ"));
    }
}
