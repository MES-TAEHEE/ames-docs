using AMES.Contracts.Dto;
using AMES.Data.Repositories;
using AMES.Data.Services;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// 10-16 사용자 결정 — 점검 완료 규칙(B4)·사이클 PM 다음 예정일·「예정」 PM 작업지시·PM 취소·보전 PM·안돈 고장 작업지시 발행
public class MntPmFlowTests
{
    const string WriteSkip = "set AMES_TEST_CONN (local DB) to run — creates and removes test PM schedules, work orders, templates and failures";
    const string Actor = "itest-mntpm";
    const string Equip = "INJ-650-01";   // 템플릿과 같은 설비 유형(INJ)
    const string Tpl   = "ITEST-PMF-T1";

    static void Cleanup(Connection.AmesConnectionFactory f) => Exec(f, """
        DELETE t FROM dbo.MNT_WorkOrderTask t JOIN dbo.MNT_WorkOrder w ON w.WorkOrderID = t.WorkOrderID WHERE w.CreatedBy = @A;
        DELETE dbo.MNT_PMExecution WHERE CreatedBy = @A;
        DELETE a FROM dbo.MNT_FailureAction a JOIN dbo.MNT_FailureRegister r ON r.FailureID = a.FailureID WHERE r.CreatedBy = @A;
        DELETE dbo.MNT_WorkOrder WHERE CreatedBy = @A;
        DELETE dbo.MNT_PMSchedule WHERE CreatedBy = @A;
        DELETE dbo.MNT_FailureRegister WHERE CreatedBy = @A;
        DELETE dbo.MD_PmTemplateStep WHERE PMTemplateID = @T;
        DELETE dbo.MD_PmTemplate WHERE PMTemplateID = @T;
        """, ("@A", Actor), ("@T", Tpl));

    static Connection.AmesConnectionFactory? WriteFactory() =>
        Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null ? null : TryFactory();

    static int? ActiveWo(Connection.AmesConnectionFactory f, int pmId) =>
        Scalar(f, "SELECT ActiveWoID FROM dbo.MNT_PMSchedule WHERE PMScheduleID = @I", ("@I", pmId)) as int?;

    static DateTime DbToday(Connection.AmesConnectionFactory f) => ((DateTime)Scalar(f, "SELECT CAST(SYSDATETIME() AS date)")!).Date;

    [SkippableFact]
    public void Checklist_blocks_ok_and_failed_items_become_a_cm_follow_up()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        var md = new MasterDataRepository(f!); var mnt = new MntRepository(f!);
        try
        {
            Cleanup(f!);
            var today = DbToday(f!);
            md.InsertPmTemplate(Tpl, "ITEST 점검", "INJ", "TIME", 30, "DAY", 30, false, true, Actor);
            md.InsertPmTemplateStep(Tpl, 1, "유압유 점검", null, null, null, 5, Actor);
            md.InsertPmTemplateStep(Tpl, 2, "필터 차압", "0.3 bar 이하", null, null, 5, Actor);

            var (pmId, _, _) = mnt.InsertPm("EQUIP", null, Equip, "MONTHLY", "TIME", 30, null, today.AddDays(2), Tpl, null, null, Actor);
            var wo = ActiveWo(f!, pmId)!.Value;
            mnt.StartWorkOrder(wo, Actor);
            var tasks = mnt.ListWorkOrderTasks(wo);

            // 미점검이 있으면 정상 완료 거부 — 아무것도 바뀌지 않는다
            var cx = Assert.Throws<MntRepository.ChecklistIncompleteException>(() => mnt.CompleteWorkOrder(wo, MwoResultCodes.Ok, "점검", null, 10, DateTime.Now, null, Actor));
            Assert.Equal((2, 0), (cx.Pending, cx.Failed));
            Assert.Equal(MwoStatusCodes.InProgress, Scalar(f!, "SELECT Status FROM dbo.MNT_WorkOrder WHERE WorkOrderID = @W", ("@W", wo)));

            mnt.SetWorkOrderTaskResult(tasks[0].WorkOrderTaskId, MntResultCodes.Pass, null, Actor);
            mnt.SetWorkOrderTaskResult(tasks[1].WorkOrderTaskId, MntResultCodes.Fail, "0.6 bar", Actor);
            Assert.Throws<MntRepository.ChecklistIncompleteException>(() => mnt.CompleteWorkOrder(wo, MwoResultCodes.Ok, "점검", null, 10, DateTime.Now, null, Actor));
            var row = Assert.Single(mnt.ListWorkOrders(500), r => r.WorkOrderId == wo);
            Assert.Equal((2, 2, 1), (row.TaskCount, row.TaskDone, row.TaskFail));

            // 후속 조치로 완료 → 불합격 항목을 담은 CM 후속 작업지시, PM 은 다음 회차로
            var o = mnt.CompleteWorkOrder(wo, MwoResultCodes.FollowUp, "필터 교체 필요", null, 10, DateTime.Now, null, Actor);
            Assert.NotNull(o.FollowUpWoNumber);
            Assert.Null(o.FailureNumber);
            Assert.NotNull(o.NextWoNumber);
            var cm = Assert.Single(mnt.ListWorkOrders(500), r => r.WoNumber == o.FollowUpWoNumber);
            Assert.Equal((MwoTypeCodes.Cm, MntRepository.CheckFollowUpSource, MwoStatusCodes.Issued, row.WoNumber), (cm.WoType, cm.SourceType, cm.Status, cm.SourceRefId));
            Assert.Contains("필터 차압 (0.6 bar)", cm.ActionDesc);
            Assert.DoesNotContain("유압유", cm.ActionDesc);

            // 후속 작업지시는 PM 회차를 넘기지 않는다(출처가 PM 이 아님) — 완료해도 다음 PM 작업지시는 그대로
            var nextWo = ActiveWo(f!, pmId);
            mnt.CompleteWorkOrder(cm.WorkOrderId, MwoResultCodes.Ok, "필터 교체", null, 15, DateTime.Now, null, Actor);
            Assert.Equal(nextWo, ActiveWo(f!, pmId));
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void Cycle_pm_needs_a_next_due_date_and_continues_with_a_new_work_order()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        var mnt = new MntRepository(f!);
        try
        {
            Cleanup(f!);
            var today = DbToday(f!);
            var (pmId, planNo, _) = mnt.InsertPm("EQUIP", null, Equip, "MONTHLY", "CYCLE", 50000, null, today, null, null, null, Actor);
            var wo = ActiveWo(f!, pmId)!.Value;
            Assert.True(Assert.Single(mnt.ListWorkOrders(500), r => r.WorkOrderId == wo).PmNeedsNextDue);

            var nx = Assert.Throws<MntRepository.PmNextDueRequiredException>(() => mnt.CompleteWorkOrder(wo, MwoResultCodes.Ok, "점검", null, 10, DateTime.Now, null, Actor));
            Assert.False(nx.Invalid);
            Assert.Throws<MntRepository.PmNextDueRequiredException>(() => mnt.CompleteWorkOrder(wo, MwoResultCodes.Ok, "점검", null, 10, DateTime.Now, null, Actor, today));
            Assert.Equal(0, Scalar(f!, "SELECT COUNT(*) FROM dbo.MNT_PMExecution WHERE PMScheduleID = @P", ("@P", pmId)));

            var o = mnt.CompleteWorkOrder(wo, MwoResultCodes.Ok, "점검", null, 10, DateTime.Now, null, Actor, today.AddDays(40));
            Assert.Equal((planNo, today.AddDays(40)), (o.PmPlanNumber, o.PmNextDue));
            Assert.NotNull(o.NextWoNumber);
            Assert.Equal((PmRules.Ok, today.AddDays(40)), ((string)Scalar(f!, "SELECT Status FROM dbo.MNT_PMSchedule WHERE PMScheduleID = @P", ("@P", pmId))!,
                (DateTime)Scalar(f!, "SELECT NextDueDate FROM dbo.MNT_PMSchedule WHERE PMScheduleID = @P", ("@P", pmId))!));

            // 종료(건너뜀)는 다음 예정일을 받지 않는다 — 활성 작업지시만 비우고, 수정에서 예정일을 넣으면 다시 발행된다
            var wo2 = ActiveWo(f!, pmId)!.Value;
            var c = mnt.CloseWorkOrder(wo2, "ITEST 건너뜀", Actor);
            Assert.Null(c.NextWoNumber);
            Assert.Null(ActiveWo(f!, pmId));
            mnt.UpdatePm(pmId, planNo, Equip, "MONTHLY", "CYCLE", 50000, today, today.AddDays(60), null, null, PmRules.Ok, Actor);
            Assert.NotNull(ActiveWo(f!, pmId));
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void Far_pm_work_orders_are_planned_and_left_out_of_open_counts_and_cancel_removes_them()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        var mnt = new MntRepository(f!);
        try
        {
            Cleanup(f!);
            var today = DbToday(f!);
            var lead  = mnt.ReadPmLeadDays();
            int OpenCount() => mnt.ListEquipment().Single(e => e.EquipId == Equip).OpenWoCount;
            var before = OpenCount();

            var (far, farNo, _)  = mnt.InsertPm("EQUIP", null, Equip, "MONTHLY", "TIME", 30, null, today.AddDays(lead + 20), null, null, null, Actor);
            var farWo = ActiveWo(f!, far)!.Value;
            var farRow = Assert.Single(mnt.ListWorkOrders(500), r => r.WorkOrderId == farWo);
            Assert.True(farRow.Planned);
            Assert.Equal((PmRules.ClassEquip, today.AddDays(lead + 20)), (farRow.PmClass, farRow.PmDueDate));
            Assert.Equal(before, OpenCount());

            var (near, _, _) = mnt.InsertPm("EQUIP", null, Equip, "MONTHLY", "TIME", 30, null, today.AddDays(lead), null, null, null, Actor);
            Assert.False(Assert.Single(mnt.ListWorkOrders(500), r => r.WorkOrderId == ActiveWo(f!, near)).Planned);
            Assert.Equal(before + 1, OpenCount());

            // 착수하면 예정이 아니다
            mnt.StartWorkOrder(farWo, Actor);
            Assert.False(Assert.Single(mnt.ListWorkOrders(500), r => r.WorkOrderId == farWo).Planned);
            Assert.Equal(before + 2, OpenCount());

            // 취소: 착수 전 작업지시는 지우고(진행 중은 남김), 다시 사용으로 바꾸면 새로 발행
            var nearWo = ActiveWo(f!, near)!.Value;
            mnt.UpdatePm(near, null, Equip, "MONTHLY", "TIME", 30, null, today.AddDays(lead), null, null, PmRules.Cancelled, Actor);
            Assert.Null(ActiveWo(f!, near));
            Assert.Equal(0, Scalar(f!, "SELECT COUNT(*) FROM dbo.MNT_WorkOrder WHERE WorkOrderID = @W", ("@W", nearWo)));
            Assert.Equal(PmRules.Cancelled, Scalar(f!, "SELECT Status FROM dbo.MNT_PMSchedule WHERE PMScheduleID = @P", ("@P", near)));
            mnt.UpdatePm(far, farNo, Equip, "MONTHLY", "TIME", 30, null, today.AddDays(lead + 20), null, null, PmRules.Cancelled, Actor);
            Assert.Equal(farWo, ActiveWo(f!, far));
            mnt.UpdatePm(near, null, Equip, "MONTHLY", "TIME", 30, null, today.AddDays(lead), null, null, PmRules.Ok, Actor);
            Assert.NotNull(ActiveWo(f!, near));
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void Maint_pm_does_not_stop_the_line_or_the_equipment()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        var mnt = new MntRepository(f!);
        var lsr = new LineScheduleRepository(f!);
        try
        {
            Cleanup(f!);
            var today = DbToday(f!);
            var line = (string)Scalar(f!, "SELECT LineID FROM dbo.MD_Equipment WHERE EquipID = @E", ("@E", Equip))!;
            var (maint, _, _) = mnt.InsertPm("MAINT", null, Equip, "MONTHLY", "TIME", 30, null, today, null, null, null, Actor);
            var (equip, _, _) = mnt.InsertPm("EQUIP", null, Equip, "MONTHLY", "TIME", 30, null, today, null, null, null, Actor);
            var cands = lsr.ListDuePmForLine(line).Select(c => c.PmScheduleId).ToList();
            Assert.Contains(equip, cands);
            Assert.DoesNotContain(maint, cands);

            var maintWo = ActiveWo(f!, maint)!.Value;
            Assert.Equal(PmRules.ClassMaint, Assert.Single(mnt.ListWorkOrders(500), r => r.WorkOrderId == maintWo).PmClass);
            mnt.StartWorkOrder(maintWo, Actor);
            Assert.Null(mnt.ListEquipment().Single(e => e.EquipId == Equip).PmWoNumber);
            mnt.StartWorkOrder(ActiveWo(f!, equip)!.Value, Actor);
            Assert.NotNull(mnt.ListEquipment().Single(e => e.EquipId == Equip).PmWoNumber);
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void Andon_failure_without_work_order_gets_one_from_the_web()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        var mnt = new MntRepository(f!);
        try
        {
            Cleanup(f!);
            var id = Convert.ToInt32(Scalar(f!, """
                INSERT dbo.MNT_FailureRegister (FailureNumber, EquipID, FailureType, Symptom, Severity, Source, Status, ReportedAt, AndonRefID, CreatedBy, CreatedTS)
                VALUES ('ITEST-PMF-01', @E, 'MOLD', N'ITEST 안돈', 'MAJOR', 'ANDON', 'OPEN', SYSDATETIME(), '999999', @A, SYSDATETIME());
                SELECT CAST(SCOPE_IDENTITY() AS int);
                """, ("@E", Equip), ("@A", Actor)));
            var woNo = mnt.IssueFailureWorkOrder(id, null, Actor);
            var fr = mnt.FindFailure(id)!;
            Assert.Equal(woNo, fr.WoNumber);
            var wo = Assert.Single(mnt.ListWorkOrders(500), r => r.WoNumber == woNo);
            Assert.Equal((MwoTypeCodes.Cm, "ANDON", "ITEST-PMF-01", MwoStatusCodes.Issued), (wo.WoType, wo.SourceType, wo.SourceRefId, wo.Status));
            Assert.Throws<InvalidOperationException>(() => mnt.IssueFailureWorkOrder(id, null, Actor));

            // 그 작업지시를 완료하면 고장도 해결된다
            mnt.CompleteWorkOrder(wo.WorkOrderId, MwoResultCodes.Ok, "금형 정비", "이물", 20, DateTime.Now, null, Actor);
            Assert.Equal("RESOLVED", mnt.FindFailure(id)!.Status);
            Assert.Throws<InvalidOperationException>(() => mnt.IssueFailureWorkOrder(id, null, Actor));
        }
        finally { Cleanup(f!); }
    }
}
