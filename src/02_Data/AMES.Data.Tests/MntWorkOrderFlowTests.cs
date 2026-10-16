using AMES.Data.Repositories;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// 10-15 사용자 결정 — 보전 작업지시 상태 MNT_STATUS(발행 → 배정 → 진행 중 → 완료 / 종료), 수동 발행, 종료 시 고장 종료·PM 회차 건너뜀
public class MntWorkOrderFlowTests
{
    const string WriteSkip = "set AMES_TEST_CONN (local DB) to run — creates and removes test work orders/failures/PM schedules";
    const string Actor = "itest-mntwo";
    const string Equip = "INJ-09-01-450";
    const string Tech  = "itest-tech";

    static void Cleanup(Connection.AmesConnectionFactory f) => Exec(f, """
        DELETE dbo.MNT_SparePartsTxn WHERE SparePartNo LIKE 'ITEST-SP-%';
        DELETE dbo.MD_SparePart WHERE SparePartNo LIKE 'ITEST-SP-%' AND CreatedBy = @A;
        DELETE a FROM dbo.MNT_FailureAction a JOIN dbo.MNT_FailureRegister r ON r.FailureID = a.FailureID WHERE r.CreatedBy = @A;
        DELETE dbo.MNT_FailureAction WHERE CreatedBy = @A;
        DELETE dbo.MNT_PMExecution WHERE CreatedBy = @A;
        DELETE dbo.MNT_WorkOrder WHERE CreatedBy = @A;
        DELETE dbo.MNT_FailureRegister WHERE CreatedBy = @A;
        DELETE dbo.MNT_PMSchedule WHERE CreatedBy = @A;
        """, ("@A", Actor));

    static Connection.AmesConnectionFactory? WriteFactory()
    {
        if (Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null) return null;
        return TryFactory();
    }

    static object? Wo(Connection.AmesConnectionFactory f, int id, string col) =>
        Scalar(f, $"SELECT {col} FROM dbo.MNT_WorkOrder WHERE WorkOrderID = @I", ("@I", id));

    [SkippableFact]
    public void Manual_issue_then_assign_and_start()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        var mnt = new MntRepository(f!);
        try
        {
            var (id, no, st) = mnt.IssueWorkOrder("PdM", Equip, "HIGH", "ITEST 진동 추세 점검", null, Actor);
            Assert.StartsWith("MWO-", no);
            Assert.Equal("ISSUED", st);
            Assert.Equal(("ISSUED", "MANUAL", "PdM"), ((string)Wo(f!, id, "Status")!, (string)Wo(f!, id, "SourceType")!, (string)Wo(f!, id, "WoType")!));

            mnt.AssignWorkOrder(id, Tech, Actor);
            Assert.Equal("ASSIGNED", Wo(f!, id, "Status"));
            mnt.StartWorkOrder(id, Actor);
            Assert.Equal("IN_PROGRESS", Wo(f!, id, "Status"));
            mnt.AssignWorkOrder(id, Tech + "2", Actor);   // 진행 중 재배정 — 담당자만 바뀐다
            Assert.Equal(("IN_PROGRESS", Tech + "2"), ((string)Wo(f!, id, "Status")!, (string)Wo(f!, id, "AssignedTechID")!));
            Assert.Throws<InvalidOperationException>(() => mnt.StartWorkOrder(id, Actor));

            var (id2, _, st2) = mnt.IssueWorkOrder("CM", Equip, "MED", "ITEST 담당자 지정 발행", Tech, Actor);
            Assert.Equal("ASSIGNED", st2);
            Assert.Equal("ASSIGNED", Wo(f!, id2, "Status"));

            Assert.Throws<ArgumentException>(() => mnt.IssueWorkOrder("CM", Equip, "MED", "  ", null, Actor));
        }
        finally { Cleanup(f!); }
    }

    // 10-15 사용자 결정 (b) — 부품 차감은 PDA 예비품 출고(작업지시 선택), 웹 완료·종료는 그 출고(취소분 제외)를 기록만 한다
    [SkippableFact]
    public void Completion_records_pda_releases_without_moving_stock()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        var mnt = new MntRepository(f!);
        try
        {
            Cleanup(f!);
            Exec(f!, """
                INSERT dbo.MD_SparePart (SparePartNo, PartNo, PartName, OnHandQty, ActiveFlag, CreatedBy, CreatedTS) VALUES
                    ('ITEST-SP-01', 'ITEST-P1', N'점검 부품 1', 5, 1, @A, SYSDATETIME()),
                    ('ITEST-SP-02', 'ITEST-P2', N'점검 부품 2', 1, 1, @A, SYSDATETIME());
                """, ("@A", Actor));
            int OnHand(string no) => Convert.ToInt32(Scalar(f!, "SELECT OnHandQty FROM dbo.MD_SparePart WHERE SparePartNo = @S", ("@S", no)));

            var (id, no, _) = mnt.IssueWorkOrder("CM", Equip, "MED", "ITEST 부품 사용", null, Actor);
            // PDA 출고(SP_PDA_STOCK_MOVE 가 작업지시를 받으면 남길 행) 3건 + 그중 1건 취소(SP_PDA_SP_STOCK_CANCEL 의 되돌림 행)
            Exec(f!, """
                INSERT dbo.MNT_SparePartsTxn (SparePartNo, MoveType, Qty, BalanceBefore, BalanceAfter, RefType, RefID, TxnAt, ActorID, CreatedBy, CreatedTS) VALUES
                    ('ITEST-SP-01', 'OUT', 1, 5, 4, 'MWO', @N, SYSDATETIME(), @A, @A, SYSDATETIME()),
                    ('ITEST-SP-01', 'OUT', 1, 4, 3, 'MWO', @N, SYSDATETIME(), @A, @A, SYSDATETIME()),
                    ('ITEST-SP-02', 'OUT', 1, 1, 0, 'MWO', @N, SYSDATETIME(), @A, @A, SYSDATETIME());
                INSERT dbo.MNT_SparePartsTxn (SparePartNo, MoveType, Qty, BalanceBefore, BalanceAfter, RefType, RefID, TxnAt, ActorID, CreatedBy, CreatedTS, ReversalOfTxnID)
                SELECT 'ITEST-SP-01', 'OUT_CANCEL', 1, 3, 4, 'PDA_CANCEL', NULL, SYSDATETIME(), @A, @A, SYSDATETIME(), MAX(SparePartsTxnID)
                FROM   dbo.MNT_SparePartsTxn WHERE SparePartNo = 'ITEST-SP-01' AND RefID = @N;
                """, ("@N", no), ("@A", Actor));

            var live = mnt.ListWoPartReleases(no);
            Assert.Equal(new[] { "ITEST-SP-01", "ITEST-SP-02" }, live.Select(r => r.SparePartNo));

            mnt.CompleteWorkOrder(id, "OK", "교체", null, 30, DateTime.Now, null, Actor);
            var used = Services.MwoPartsUsed.Parse((string)Wo(f!, id, "PartsUsedJSON")!);
            Assert.Equal(new[] { ("ITEST-SP-01", 1, "ITEST-P1"), ("ITEST-SP-02", 1, "ITEST-P2") }, used.Select(l => (l.SparePartNo, l.Qty, l.PartNo!)));
            Assert.Equal((5, 1), (OnHand("ITEST-SP-01"), OnHand("ITEST-SP-02")));   // 웹 완료는 재고를 움직이지 않는다
            Assert.Equal(4, Convert.ToInt32(Scalar(f!, "SELECT COUNT(*) FROM dbo.MNT_SparePartsTxn WHERE SparePartNo LIKE 'ITEST-SP-%'")));

            // 출고가 없는 작업지시는 기존 값을 그대로 둔다(옛 자유 입력 보존)
            var (id2, _, _) = mnt.IssueWorkOrder("CM", Equip, "MED", "ITEST 부품 없음", null, Actor);
            Exec(f!, "UPDATE dbo.MNT_WorkOrder SET PartsUsedJSON = N'[\"VLV-12\"]' WHERE WorkOrderID = @I", ("@I", id2));
            mnt.CloseWorkOrder(id2, "중복", Actor);
            Assert.Equal("[\"VLV-12\"]", Wo(f!, id2, "PartsUsedJSON"));
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void Closing_a_failure_work_order_closes_the_failure_without_resolution()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        var mnt = new MntRepository(f!);
        try
        {
            var (failId, failNo, _, _) = mnt.InsertFailure(null, Equip, "ELEC", "ITEST 종료", "MAJOR", "WEB", DateTime.Now.AddHours(-1), null, Actor);
            var woId = Convert.ToInt32(Scalar(f!, "SELECT WorkOrderID FROM dbo.MNT_FailureRegister WHERE FailureID = @I", ("@I", failId)));
            Assert.Equal("ISSUED", Wo(f!, woId, "Status"));
            mnt.AssignWorkOrder(woId, Tech, Actor);
            Assert.Equal("ASSIGNED", Wo(f!, woId, "Status"));

            Assert.Throws<ArgumentException>(() => mnt.CloseWorkOrder(woId, " ", Actor));
            var o = mnt.CloseWorkOrder(woId, "중복 등록", Actor);

            Assert.Equal(failNo, o.FailureNumber);
            Assert.Null(o.PmPlanNumber);
            Assert.Equal("CLOSED", Wo(f!, woId, "Status"));
            Assert.NotEqual(DBNull.Value, Wo(f!, woId, "ClosedAt"));
            Assert.Equal(DBNull.Value, Wo(f!, woId, "CompletedAt"));
            var res = MntRepository.ParseResult((string)Wo(f!, woId, "ChecklistResultsJSON")!);
            Assert.Equal(("CLOSED", "중복 등록"), (res?.Result, res?.Action));

            Assert.Equal("CLOSED", Scalar(f!, "SELECT Status FROM dbo.MNT_FailureRegister WHERE FailureID = @I", ("@I", failId)));
            Assert.Equal(DBNull.Value, Scalar(f!, "SELECT ResolvedAt FROM dbo.MNT_FailureRegister WHERE FailureID = @I", ("@I", failId)));
            Assert.Equal(1, Convert.ToInt32(Scalar(f!, "SELECT COUNT(*) FROM dbo.MNT_FailureAction WHERE FailureID = @I AND ActionType = 'CLOSED' AND Description = N'중복 등록' AND WorkOrderID = @W",
                ("@I", failId), ("@W", woId))));

            // 끝난 작업지시·고장은 다시 닫거나 완료·수리 완료할 수 없다
            Assert.Throws<InvalidOperationException>(() => mnt.CloseWorkOrder(woId, "다시", Actor));
            Assert.Throws<InvalidOperationException>(() => mnt.CompleteWorkOrder(woId, "OK", "조치", null, 5, DateTime.Now, null, Actor));
            Assert.Throws<InvalidOperationException>(() => mnt.AssignWorkOrder(woId, Tech, Actor));
            Assert.Throws<InvalidOperationException>(() => mnt.ResolveFailure(failId, "원인", "조치", DateTime.Now, 5, Actor));
            Assert.Equal(Services.FailureDeleteRules.Block.NotOpen,
                Assert.Throws<MntRepository.FailureNotDeletableException>(() => mnt.DeleteFailure(failId)).Reason);
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void Closing_a_pm_work_order_skips_this_cycle()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        var mnt = new MntRepository(f!);
        try
        {
            var today = DateTime.Today;
            var lastPm = today.AddDays(-25);
            var due    = today.AddDays(5);
            var (pmId, planNo, _) = mnt.InsertPm("EQUIP", null, Equip, "MONTHLY", "TIME", 30, lastPm, due, null, Tech, "OK", Actor);
            var woId = Convert.ToInt32(Scalar(f!, "SELECT ActiveWoID FROM dbo.MNT_PMSchedule WHERE PMScheduleID = @I", ("@I", pmId)));
            Assert.Equal("ASSIGNED", Wo(f!, woId, "Status"));   // 담당자 있는 PM 의 작업지시는 배정 상태로 발행

            var o = mnt.CloseWorkOrder(woId, "설비 이설로 이번 회차 생략", Actor);

            Assert.Equal(planNo, o.PmPlanNumber);
            Assert.Equal(due.AddDays(30), o.PmNextDue);
            Assert.NotNull(o.NextWoNumber);
            Assert.Equal(lastPm, (DateTime)Scalar(f!, "SELECT LastPMDate FROM dbo.MNT_PMSchedule WHERE PMScheduleID = @I", ("@I", pmId))!);
            Assert.Equal(due.AddDays(30), (DateTime)Scalar(f!, "SELECT NextDueDate FROM dbo.MNT_PMSchedule WHERE PMScheduleID = @I", ("@I", pmId))!);

            var nextWo = Convert.ToInt32(Scalar(f!, "SELECT ActiveWoID FROM dbo.MNT_PMSchedule WHERE PMScheduleID = @I", ("@I", pmId)));
            Assert.NotEqual(woId, nextWo);
            Assert.Equal(("ASSIGNED", o.NextWoNumber), ((string)Wo(f!, nextWo, "Status")!, (string)Wo(f!, nextWo, "WoNumber")!));

            var exec = Assert.Single(mnt.ListPmExecutionsFor(pmId));
            Assert.Equal(("CLOSED", "설비 이설로 이번 회차 생략", (int?)woId), (exec.Result, exec.ResultNote, exec.WorkOrderId));
        }
        finally { Cleanup(f!); }
    }
}
