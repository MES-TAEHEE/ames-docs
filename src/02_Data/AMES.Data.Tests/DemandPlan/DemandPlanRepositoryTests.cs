using AMES.Data.Connection;
using AMES.Data.Repositories;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests.DemandPlan;

/// <summary>PP_DemandPlan 날짜 창 교체 저장(스펙 §3.3) · 배치 헤더 · 조회. AMES_DEV 통합, DB 미기동 시 skip.
/// 시드: 고객 ITEST-DPC(MD_Customer 없이도 저장된다 — FK 없음), 품번 ITEST-DP-A(MD_Item BoxQty 20) · ITEST-DP-B(미등록).
/// 정리는 ITEST-DP 접두어 행만.</summary>
[Collection("AMES_DEV plan week")]
public class DemandPlanRepositoryTests
{
    const string Cust = "ITEST-DPC";
    const string A = "ITEST-DP-A", B = "ITEST-DP-B";
    const string Actor = "ITEST-DP";
    static readonly DateOnly D0 = new(2027, 3, 1);   // 월요일, 다른 테스트와 겹치지 않는 먼 날짜

    static void Seed(AmesConnectionFactory f)
    {
        Cleanup(f);
        Exec(f, """
            INSERT INTO dbo.MD_Item (ItemNo, ItemName, RoutingType, SafetyStock, BoxQty, ActiveFlag, CreatedBy)
            VALUES (@A, N'ITEST DP A', 'A', 0, 20, 1, @By);
            """, ("@A", A), ("@By", Actor));
    }

    static void Cleanup(AmesConnectionFactory f) => Exec(f, """
        DELETE FROM dbo.PP_DemandPlan      WHERE CustomerID = @C OR ItemNo LIKE 'ITEST-DP-%';
        DELETE FROM dbo.PP_DemandPlanBatch WHERE CustomerID = @C;
        DELETE FROM dbo.MD_Item            WHERE ItemNo LIKE 'ITEST-DP-%' AND CreatedBy = @By;
        """, ("@C", Cust), ("@By", Actor));

    static PpRepository.DemandPlanCell Cell(string item, int dayOffset, decimal sched, decimal po = 0, decimal? pack = 20)
        => new(item, "NAME", "EA", pack, D0.AddDays(dayOffset), sched, po);

    static int Count(AmesConnectionFactory f, string item, DateOnly? d = null)
        => (int)Scalar(f, "SELECT COUNT(*) FROM dbo.PP_DemandPlan WHERE CustomerID = @C AND ItemNo = @I AND (@D IS NULL OR PlanDate = @D)",
               ("@C", Cust), ("@I", item), ("@D", (object?)d?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value))!;

    [SkippableFact]
    public void Replace_window_removes_items_missing_from_the_new_file()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Seed(f!);
        try
        {
            var repo = new PpRepository(f!);
            var r1 = repo.ReplaceDemandPlan(Cust, "DPTEST0001", PpRepository.DemandPlanSourceUpload, null, "first.xlsx", D0, D0.AddDays(4),
                new[] { Cell(A, 0, 100), Cell(A, 1, 0), Cell(A, 2, 50, po: 10), Cell(B, 0, 30, pack: null), Cell(A, 10, 999) }, Actor);
            Assert.Equal(2, r1.ItemCount);
            Assert.Equal(3, r1.RowCount);          // 0 은 저장 안 함, 창 밖(D+10)은 무시
            Assert.Equal(1, r1.UnmatchedItems);     // B
            Assert.Equal(0, r1.PackMismatch);
            Assert.Equal(0, Count(f!, A, D0.AddDays(1)));
            Assert.Equal(0, Count(f!, A, D0.AddDays(10)));

            // 두 번째 파일: B 가 빠지고 A 의 D0 가 0 이 됨 → 둘 다 사라져야 한다. 창 밖에 미리 둔 행은 보존.
            Exec(f!, """
                INSERT INTO dbo.PP_DemandPlan (CustomerID, ItemNo, PlanDate, ScheduledQty, Batch, Source, CreatedBy)
                VALUES (@C, @A, @D, 7, 'DPTEST0001', 'UPLOAD', @By);
                """, ("@C", Cust), ("@A", A), ("@D", D0.AddDays(20).ToDateTime(TimeOnly.MinValue)), ("@By", Actor));
            var r2 = repo.ReplaceDemandPlan(Cust, "DPTEST0002", PpRepository.DemandPlanSourceUpload, null, "second.xlsx", D0, D0.AddDays(4),
                new[] { Cell(A, 0, 0), Cell(A, 2, 60, pack: 24) }, Actor);
            Assert.Equal(1, r2.RowCount);
            Assert.Equal(1, r2.PackMismatch);       // 24 ≠ BoxQty 20
            Assert.Equal(0, Count(f!, B));
            Assert.Equal(0, Count(f!, A, D0));
            Assert.Equal(60m, Scalar(f!, "SELECT ScheduledQty FROM dbo.PP_DemandPlan WHERE CustomerID=@C AND ItemNo=@I AND PlanDate=@D",
                ("@C", Cust), ("@I", A), ("@D", D0.AddDays(2).ToDateTime(TimeOnly.MinValue))));
            Assert.Equal(1, Count(f!, A, D0.AddDays(20)));   // 창 밖 보존

            var batches = repo.ListDemandPlanBatches(Cust);
            Assert.Equal(new[] { "DPTEST0002", "DPTEST0001" }, batches.Select(b => b.Batch).ToArray());
            Assert.Equal("second.xlsx", batches[0].FileName);
            Assert.Equal((D0, D0.AddDays(4)), (batches[0].DateFrom, batches[0].DateTo));

            var cells = repo.ListDemandPlan(Cust, D0, D0.AddDays(30));
            var a2 = Assert.Single(cells, c => c.ItemNo == A && c.PlanDate == D0.AddDays(2));
            Assert.True(a2.ItemExists); Assert.Equal(20m, a2.BoxQty); Assert.Equal(24m, a2.PackQty);
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void Replace_rolls_back_whole_batch_on_bad_row()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Seed(f!);
        try
        {
            var repo = new PpRepository(f!);
            repo.ReplaceDemandPlan(Cust, "DPTEST0003", PpRepository.DemandPlanSourceUpload, null, null, D0, D0, new[] { Cell(A, 0, 5) }, Actor);
            Assert.ThrowsAny<Exception>(() => repo.ReplaceDemandPlan(Cust, "DPTEST0004", PpRepository.DemandPlanSourceUpload, null, null, D0, D0,
                new[] { Cell(A, 0, 9), Cell(new string('X', 25), 0, 1) }, Actor));   // ItemNo 25자 → varchar(20) 초과
            Assert.Equal(5m, Scalar(f!, "SELECT ScheduledQty FROM dbo.PP_DemandPlan WHERE CustomerID=@C AND ItemNo=@I", ("@C", Cust), ("@I", A)));
            Assert.Equal(0, Scalar(f!, "SELECT COUNT(*) FROM dbo.PP_DemandPlanBatch WHERE Batch = 'DPTEST0004'"));
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void Replace_sums_duplicate_item_date_cells()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Seed(f!);
        try
        {
            var repo = new PpRepository(f!);
            var r = repo.ReplaceDemandPlan(Cust, "DPTEST0005", PpRepository.DemandPlanSourceUpload, null, null, D0, D0,
                new[] { Cell(A, 0, 40, po: 3), Cell(A, 0, 60, po: 7) }, Actor);
            Assert.Equal(1, r.ItemCount);
            Assert.Equal(1, r.RowCount);        // 두 셀이 한 행으로 합쳐진다
            Assert.Equal(1, Count(f!, A, D0));
            Assert.Equal(100m, Scalar(f!, "SELECT ScheduledQty FROM dbo.PP_DemandPlan WHERE CustomerID=@C AND ItemNo=@I AND PlanDate=@D",
                ("@C", Cust), ("@I", A), ("@D", D0.ToDateTime(TimeOnly.MinValue))));
            Assert.Equal(10m, Scalar(f!, "SELECT PoQty FROM dbo.PP_DemandPlan WHERE CustomerID=@C AND ItemNo=@I AND PlanDate=@D",
                ("@C", Cust), ("@I", A), ("@D", D0.ToDateTime(TimeOnly.MinValue))));
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void Replace_drops_negative_scheduled_qty_cells()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Seed(f!);
        try
        {
            var repo = new PpRepository(f!);
            var r = repo.ReplaceDemandPlan(Cust, "DPTEST0006", PpRepository.DemandPlanSourceUpload, null, null, D0, D0,
                new[] { Cell(A, 0, -5) }, Actor);
            Assert.Equal(0, r.RowCount);
            Assert.Equal(0, Count(f!, A, D0));
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void ListItemBoxQty_returns_only_items_with_box_qty()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Seed(f!);
        try
        {
            var repo = new PpRepository(f!);
            var map = repo.ListItemBoxQty(new[] { A, B });
            Assert.Equal(20m, map[A]);
            Assert.False(map.ContainsKey(B));
        }
        finally { Cleanup(f!); }
    }
}
