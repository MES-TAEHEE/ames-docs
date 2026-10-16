using AMES.Contracts.Dto;
using AMES.Data.Repositories;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// 10-15 사용자 결정 — PM 템플릿 단계(필요 부품 = 예비품)를 PM 작업지시 점검 항목으로 복사, 결과는 공통코드 MNT_RESULT
public class MntPmChecklistTests
{
    const string WriteSkip = "set AMES_TEST_CONN (local DB) to run — creates and removes a test template, spare part, PM schedules and work orders";
    const string Actor = "itest-mntchk";
    const string Equip = "INJ-01-01-1800";   // 템플릿과 같은 설비 유형(INJ)
    const string OtherTypeEquip = "ITEST-EQ-WRAP";   // 시험 안에서 만들고 지우는 WRAP 설비
    const string Tpl1  = "ITEST-CHK-T1";
    const string Tpl2  = "ITEST-CHK-T2";
    const string Part  = "ITEST-SP-CHK";

    [Theory]
    [InlineData(null,  null, null,  null)]
    [InlineData("  ",  5,    null,  null)]   // 부품 없으면 수량도 비운다
    [InlineData("SP1", 2,    "SP1", 2)]
    [InlineData(" SP1 ", 1,  "SP1", 1)]
    public void Step_part_rule(string? part, int? qty, string? expPart, int? expQty)
        => Assert.Equal((expPart, expQty), MasterDataRepository.NormalizeStepPart(part, qty));

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void Part_without_positive_qty_is_rejected(int? qty)
        => Assert.Throws<ArgumentException>(() => MasterDataRepository.NormalizeStepPart("SP1", qty));

    // 10-16 — PM 일정에는 설비와 설비 유형이 같은 템플릿만
    [Theory]
    [InlineData("INJ",  "INJ",   true)]
    [InlineData("inj ", "INJ",   true)]
    [InlineData("INJ",  "WRAP",  false)]
    [InlineData(null,   "INJ",   false)]
    [InlineData("INJ",  null,    false)]
    [InlineData("",     "",      false)]
    public void Template_matches_only_the_same_equipment_type(string? tplType, string? equipType, bool expected)
        => Assert.Equal(expected, MntRepository.TemplateMatchesEquipment(tplType, equipType));

    static void Cleanup(Connection.AmesConnectionFactory f) => Exec(f, """
        DELETE t FROM dbo.MNT_WorkOrderTask t JOIN dbo.MNT_WorkOrder w ON w.WorkOrderID = t.WorkOrderID WHERE w.CreatedBy = @A;
        DELETE dbo.MNT_PMExecution WHERE CreatedBy = @A;
        DELETE dbo.MNT_WorkOrder WHERE CreatedBy = @A;
        DELETE dbo.MNT_PMSchedule WHERE CreatedBy = @A;
        DELETE dbo.MD_PmTemplateStep WHERE PMTemplateID IN (@T1, @T2);
        DELETE dbo.MD_PmTemplate WHERE PMTemplateID IN (@T1, @T2);
        DELETE dbo.MD_SparePart WHERE SparePartNo = @P;
        DELETE dbo.MD_Equipment WHERE EquipID = @E;
        """, ("@A", Actor), ("@T1", Tpl1), ("@T2", Tpl2), ("@P", Part), ("@E", OtherTypeEquip));

    static Connection.AmesConnectionFactory? WriteFactory()
    {
        if (Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null) return null;
        return TryFactory();
    }

    static int ActiveWo(Connection.AmesConnectionFactory f, int pmId) =>
        Convert.ToInt32(Scalar(f, "SELECT ActiveWoID FROM dbo.MNT_PMSchedule WHERE PMScheduleID = @I", ("@I", pmId)));

    [SkippableFact]
    public void Template_steps_are_copied_to_pm_work_orders_and_results_are_recorded_while_in_progress()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        var md  = new MasterDataRepository(f!);
        var mnt = new MntRepository(f!);
        try
        {
            Cleanup(f!);
            Exec(f!, "INSERT dbo.MD_Equipment (EquipID, EquipName, EquipType, LineID, Status, ActiveFlag, CreatedBy, CreatedTS) VALUES (@E, N'ITEST 감싸기', 'WRAP', 'LINE-IMG-01', 'RUN', 1, @A, SYSDATETIME())",
                ("@E", OtherTypeEquip), ("@A", Actor));
            Exec(f!, "INSERT dbo.MD_SparePart (SparePartNo, PartNo, PartName, OnHandQty, ActiveFlag, CreatedBy, CreatedTS) VALUES (@P, 'ITEST-PCHK', N'점검 부품', 5, 1, @A, SYSDATETIME())",
                ("@P", Part), ("@A", Actor));
            md.InsertPmTemplate(Tpl1, "ITEST 월간", "INJ", "TIME", 30, "DAY", 60, false, true, Actor);
            md.InsertPmTemplate(Tpl2, "ITEST 분기", "INJ", "TIME", 90, "DAY", 90, false, true, Actor);

            // 필요 부품은 마스터에 있는 예비품만, 고르면 수량 1 이상
            Assert.Throws<InvalidOperationException>(() => md.InsertPmTemplateStep(Tpl1, 9, "없는 부품", null, "ITEST-SP-NONE", 1, null, Actor));
            Assert.Throws<ArgumentException>(() => md.InsertPmTemplateStep(Tpl1, 9, "수량 없음", null, Part, null, null, Actor));
            md.InsertPmTemplateStep(Tpl1, 1, "유압유 점검", "레벨 MIN 이상", null, null, 10, Actor);
            var s2 = md.InsertPmTemplateStep(Tpl1, 2, "필터 교체", "차압 0.3 bar 이하", Part, 2, 20, Actor);
            md.InsertPmTemplateStep(Tpl2, 1, "정밀 점검", null, null, null, 45, Actor);
            var step = Assert.Single(md.ListPmTemplateSteps(Tpl1), s => s.PMStepID == s2);
            Assert.Equal((Part, 2, "ITEST-PCHK"), (step.RequiredSparePartNo!, step.RequiredQty!.Value, step.RequiredPartNo!));

            // 템플릿이 쓰는 예비품은 지울 수 없다
            Assert.Throws<InvalidOperationException>(() => md.DeleteSparePart(Part));

            // 설비 유형이 다른 템플릿은 등록·수정 모두 거부(아무것도 쓰지 않음)
            Assert.Throws<MntRepository.TemplateTypeMismatchException>(() =>
                mnt.InsertPm("EQUIP", null, OtherTypeEquip, "MONTHLY", "TIME", 30, null, DateTime.Today.AddDays(5), Tpl1, null, "OK", Actor));
            Assert.Equal(0, Convert.ToInt32(Scalar(f!, "SELECT COUNT(*) FROM dbo.MNT_PMSchedule WHERE CreatedBy = @A", ("@A", Actor))));

            // 등록 = 작업지시 발행 + 단계 복사(미점검)
            var (pmId, _, _) = mnt.InsertPm("EQUIP", null, Equip, "MONTHLY", "TIME", 30, DateTime.Today.AddDays(-25), DateTime.Today.AddDays(5), Tpl1, null, "OK", Actor);
            var wo = ActiveWo(f!, pmId);
            var tasks = mnt.ListWorkOrderTasks(wo);
            Assert.Equal(new[] { "유압유 점검", "필터 교체" }, tasks.Select(t => t.TaskName));
            Assert.All(tasks, t => Assert.Equal(MntResultCodes.Pending, t.Result));
            var copied = tasks[1];
            Assert.Equal((s2, "차압 0.3 bar 이하", Part, 2, 20), (copied.PmStepId!, copied.AcceptanceCriteria!, copied.RequiredSparePartNo!, copied.RequiredQty!.Value, copied.StepDurationMin!.Value));

            Assert.Throws<MntRepository.TemplateTypeMismatchException>(() =>
                mnt.UpdatePm(pmId, null, OtherTypeEquip, "MONTHLY", "TIME", 30, DateTime.Today.AddDays(-25), DateTime.Today.AddDays(5), Tpl1, null, "OK", Actor));
            Assert.Equal(Equip, Scalar(f!, "SELECT EquipID FROM dbo.MNT_PMSchedule WHERE PMScheduleID = @I", ("@I", pmId)));

            // 착수 전 템플릿을 바꾸면 점검 항목도 새 템플릿으로
            mnt.UpdatePm(pmId, null, Equip, "MONTHLY", "TIME", 30, DateTime.Today.AddDays(-25), DateTime.Today.AddDays(5), Tpl2, null, "OK", Actor);
            Assert.Equal(new[] { "정밀 점검" }, mnt.ListWorkOrderTasks(wo).Select(t => t.TaskName));
            mnt.UpdatePm(pmId, null, Equip, "MONTHLY", "TIME", 30, DateTime.Today.AddDays(-25), DateTime.Today.AddDays(5), Tpl1, null, "OK", Actor);
            tasks = mnt.ListWorkOrderTasks(wo);
            Assert.Equal(2, tasks.Count);

            // 결과는 진행 중에만
            Assert.Throws<InvalidOperationException>(() => mnt.SetWorkOrderTaskResult(tasks[0].WorkOrderTaskId, MntResultCodes.Pass, null, Actor));
            mnt.StartWorkOrder(wo, Actor);
            Assert.Throws<ArgumentException>(() => mnt.SetWorkOrderTaskResult(tasks[0].WorkOrderTaskId, "OK", null, Actor));
            mnt.SetWorkOrderTaskResult(tasks[0].WorkOrderTaskId, MntResultCodes.Pass, null, Actor);
            mnt.SetWorkOrderTaskResult(tasks[1].WorkOrderTaskId, MntResultCodes.Fail, "차압 0.5 bar", Actor);
            tasks = mnt.ListWorkOrderTasks(wo);
            Assert.Equal((MntResultCodes.Fail, "차압 0.5 bar", Actor), (tasks[1].Result, tasks[1].Note, tasks[1].CompletedBy));
            Assert.NotNull(tasks[1].CompletedAt);
            var row = Assert.Single(mnt.ListWorkOrders(500), r => r.WorkOrderId == wo);
            Assert.Equal((2, 2), (row.TaskDone, row.TaskCount));

            // 미점검으로 되돌리면 점검자·시각을 지운다
            mnt.SetWorkOrderTaskResult(tasks[1].WorkOrderTaskId, MntResultCodes.Pending, null, Actor);
            var back = mnt.ListWorkOrderTasks(wo)[1];
            Assert.Equal((MntResultCodes.Pending, (string?)null, (DateTime?)null), (back.Result, back.CompletedBy, back.CompletedAt));

            // 미점검이 남으면 정상 완료는 거부(10-16 B4), 해당 없음(NA)은 점검한 것으로 본다
            Assert.Throws<MntRepository.ChecklistIncompleteException>(() => mnt.CompleteWorkOrder(wo, "OK", "점검 완료", null, 60, DateTime.Now, null, Actor));
            mnt.SetWorkOrderTaskResult(tasks[1].WorkOrderTaskId, MntResultCodes.Na, null, Actor);

            // 완료 → 다음 회차 작업지시에도 단계가 복사된다, 끝난 작업지시에는 결과를 쓸 수 없다
            var o = mnt.CompleteWorkOrder(wo, "OK", "점검 완료", null, 60, DateTime.Now, null, Actor);
            Assert.NotNull(o.NextWoNumber);
            Assert.Equal(2, mnt.ListWorkOrderTasks(ActiveWo(f!, pmId)).Count);
            Assert.Throws<InvalidOperationException>(() => mnt.SetWorkOrderTaskResult(tasks[0].WorkOrderTaskId, MntResultCodes.Na, null, Actor));

            // PM 삭제는 착수 전 작업지시와 그 점검 항목을 같이 지운다
            var next = ActiveWo(f!, pmId);
            mnt.DeletePm(pmId);
            Assert.Equal(0, Convert.ToInt32(Scalar(f!, "SELECT COUNT(*) FROM dbo.MNT_WorkOrderTask WHERE WorkOrderID = @W", ("@W", next))));
            Assert.Equal(2, mnt.ListWorkOrderTasks(wo).Count);   // 완료된 작업지시의 기록은 남는다
        }
        finally { Cleanup(f!); }
    }
}
