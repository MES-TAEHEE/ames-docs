using AMES.Data.Repositories;
using Xunit;

namespace AMES.Data.Tests;

/// <summary>QC 검사 대상 = 스캔한 LOT(품번·WO·라인) 또는 품번 — 검사 행에 LOT·WO·라인이 남는다(10-28). AMES_TEST_CONN 이 닿을 때만 돈다.</summary>
public class QcInspectionTargetDbTests
{
    const string Lot  = "ITQCT0001";
    const string Item = "ITEST-QCT";

    [SkippableFact]
    public void Lot_scan_resolves_item_wo_and_line_and_the_inspection_keeps_them()
    {
        var f = AmesDevDb.TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Clean(f!);
        try
        {
            AmesDevDb.Exec(f!, """
                INSERT INTO dbo.MD_Item (ItemNo, ItemName, ActiveFlag, CreatedBy) VALUES (@I, N'ITEST QC target', 1, 'ITEST');
                INSERT INTO dbo.tbl_Lot (LotCode, ItemNo, LineID, ProcessCode, WoID, BatchSize, RemainingQty, ProducedAt, Status, QualityFlag, CreatedBy, CreatedTS)
                VALUES (@L, @I, 'LINE-INJ-01', 'INJ', 987654, 1, 1, SYSDATETIME(), 'CONFIRMED', 'OK', 'ITEST', SYSDATETIME());
                """, ("@I", Item), ("@L", Lot));
            var repo = new QcRepository(f!);

            var byLot = repo.ResolveInspectionTarget(Lot);
            Assert.NotNull(byLot);
            Assert.Equal((Item, (int?)987654, "LINE-INJ-01"), (byLot!.ItemNo, byLot.WoId, byLot.LineId));
            Assert.NotNull(byLot.LotId);

            var byItem = repo.ResolveInspectionTarget(Item);
            Assert.Equal((Item, (int?)null, (int?)null), (byItem!.ItemNo, byItem.LotId, byItem.WoId));
            Assert.Null(repo.ResolveInspectionTarget("ITEST-QCT-NONE"));

            var id = repo.CreateInspection("InProcess", byLot, "LINE-IMG-01", 20, "ITEST", "ITEST");
            var row = AmesDevDb.Scalar(f!, "SELECT CONCAT(ItemNo, '|', IIF(LotID = @Lot, 'lot', 'x'), '|', WoID, '|', LineID) FROM dbo.QC_Inspection WHERE InspectionID = @Id",
                                       ("@Lot", byLot.LotId!), ("@Id", id));
            Assert.Equal($"{Item}|lot|987654|LINE-INJ-01", row);   // LOT 의 라인이 터미널 라인보다 우선

            var id2 = repo.CreateInspection("Incoming", byItem, "LINE-IMG-01", 32, "ITEST", "ITEST");
            Assert.Equal("LINE-IMG-01", AmesDevDb.Scalar(f!, "SELECT LineID FROM dbo.QC_Inspection WHERE InspectionID = @Id", ("@Id", id2)));
        }
        finally { Clean(f!); }
    }

    static void Clean(AMES.Data.Connection.AmesConnectionFactory f)
        => AmesDevDb.Exec(f, """
            DELETE dbo.QC_Inspection WHERE ItemNo = @I AND CreatedBy = 'ITEST';
            DELETE dbo.tbl_Lot       WHERE LotCode = @L;
            DELETE dbo.MD_Item       WHERE ItemNo = @I;
            """, ("@I", Item), ("@L", Lot));
}
