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

    [SkippableFact]
    public void Lot_detail_lists_every_process_station_and_operator_of_its_work_order()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, "set AMES_TEST_CONN to run");
        var f = TryFactory(); Skip.If(f is null, "no dev db");
        // 두 공정 이상 실적이 있는 작업지시의 한 LOT
        var lot = Scalar(f!, @"SELECT TOP 1 l.LotCode FROM dbo.tbl_Lot l WHERE l.WoID IN
            (SELECT r.WoID FROM dbo.PR_ProductionResult r WHERE r.OperatorID IS NOT NULL GROUP BY r.WoID HAVING COUNT(DISTINCT r.ProcessCode) >= 2) ORDER BY l.LotID DESC") as string;
        Skip.If(lot is null, "no work order with two processes");
        var detail = new WorkOrderRepository(f!).GetLotDetail(lot!);
        Assert.NotNull(detail);
        Assert.True(detail!.ProcessHistory!.Select(h => h.ProcessCode).Distinct().Count() >= 2);
        Assert.Contains(detail.ProcessHistory!, h => h.HasThisLot);
    }

    [SkippableFact]
    public void Full_lot_list_has_every_lot_with_operator_and_station()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, "set AMES_TEST_CONN to run");
        var f = TryFactory(); Skip.If(f is null, "no dev db");
        var woId = Scalar(f!, "SELECT TOP 1 l.WoID FROM dbo.tbl_Lot l WHERE l.WoID IS NOT NULL GROUP BY l.WoID ORDER BY COUNT(*) DESC") as int?;
        Skip.If(woId is null, "no work order with lots");
        var repo = new WorkOrderRepository(f!);
        var full = repo.ListWoLotsFull(woId!.Value);
        var chips = repo.ListWoLots(woId.Value);
        Assert.True(full.Count >= chips.Count);                       // 칩 목록(최대 300)보다 많거나 같다
        Assert.Contains(full, r => !string.IsNullOrEmpty(r.OperatorId));
        Assert.Equal(full.Select(r => r.LotCode).Distinct().Count(), full.Count);   // LOT 번호 중복 없음
    }
}
