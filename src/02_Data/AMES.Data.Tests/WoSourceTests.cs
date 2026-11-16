using AMES.Data.Repositories;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// WO 생성 경로 기록 — 수동 발행은 MANUAL, 추적 조회는 그 WO 의 수주·슬롯·실적을 한 줄로 돌려준다
public class WoSourceTests
{
    const string WriteSkip = "set AMES_TEST_CONN (local DB) to run — creates and removes a test work order";

    [SkippableFact]
    public void Manual_work_order_records_its_source_and_trace_is_readable()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = TryFactory(); Skip.If(f is null, WriteSkip);
        var item = Scalar(f!, "SELECT TOP 1 ItemNo FROM dbo.MD_Item WHERE RoutingType IS NOT NULL ORDER BY ItemNo") as string;
        Skip.If(item is null, "no item with a routing type");
        var wos = new WorkOrderRepository(f!);
        var wo = wos.CreateManualWo(item!, 5m, DateTime.Today, "itest-src");
        Assert.NotEmpty(wo);
        try
        {
            var id = (int)Scalar(f!, "SELECT WoID FROM dbo.PP_WorkOrder WHERE WoNumber = @W", ("@W", wo))!;
            Assert.Equal("MANUAL", Scalar(f!, "SELECT SourceType FROM dbo.PP_WorkOrder WHERE WoID = @I", ("@I", id)));
            var t = wos.GetTrace(id);
            Assert.NotNull(t);
            Assert.Equal("MANUAL", t!.SourceType);
            Assert.Equal(0, t.SlotCount);
            Assert.Equal(0m, t.GoodQty);
        }
        finally { Exec(f!, "DELETE dbo.PP_WorkOrder WHERE WoNumber = @W", ("@W", wo)); }
    }

    [SkippableFact]
    public void Step_operators_lists_open_pop_sessions_of_the_step_lines()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = TryFactory(); Skip.If(f is null, WriteSkip);
        var woId = Scalar(f!, "SELECT TOP 1 WoID FROM dbo.PP_WorkOrderRouting WHERE LineID IS NOT NULL ORDER BY WoID DESC") as int?;
        Skip.If(woId is null, "no work order with a routing line");
        var line = (string)Scalar(f!, "SELECT TOP 1 LineID FROM dbo.PP_WorkOrderRouting WHERE WoID = @W AND LineID IS NOT NULL", ("@W", woId!.Value))!;
        Exec(f!, "DELETE dbo.PR_PopSession WHERE OperatorID = 'ITEST-OP'");
        try
        {
            Exec(f!, "INSERT dbo.PR_PopSession (OperatorID, TerminalID, LineID, StartedAt, ExpiresAt, CreatedBy) VALUES ('ITEST-OP', 'ST-ITEST', @L, SYSDATETIME(), DATEADD(hour, 1, SYSDATETIME()), 'itest')", ("@L", line));
            var ops = new WorkOrderRepository(f!).ListStepOperators(woId.Value);
            Assert.Contains(ops.Single(o => o.LineId == line).Sessions, s => s.Operator == "ITEST-OP" && s.Terminal == "ST-ITEST");

            // 로그아웃한 세션은 현재 작업자가 아니다
            Exec(f!, "UPDATE dbo.PR_PopSession SET LoggedOutAt = SYSDATETIME() WHERE OperatorID = 'ITEST-OP'");
            ops = new WorkOrderRepository(f!).ListStepOperators(woId.Value);
            Assert.DoesNotContain(ops.Single(o => o.LineId == line).Sessions, s => s.Operator == "ITEST-OP");
        }
        finally { Exec(f!, "DELETE dbo.PR_PopSession WHERE OperatorID = 'ITEST-OP'"); }
    }
}
