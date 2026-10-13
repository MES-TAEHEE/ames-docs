using AMES.Data.Repositories;
using AMES.Data.Services;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// 10-14 사용자 결정 — 끝난 WO(완료·입고·마감·취소)는 PP-CAL 에서 납기를 바꾸지 않는다(MRP·APS 의 열린 WO 기준과 같음)
public class WoStatusRulesTests
{
    const string WriteSkip = "set AMES_TEST_CONN (local DB) to run — creates and removes test work orders";
    const string Actor = "itest-duedate";

    [Theory]
    [InlineData("Draft", false)]
    [InlineData("Planned", false)]
    [InlineData("Released", false)]
    [InlineData("In Progress", false)]
    [InlineData(null, false)]
    [InlineData("Completed", true)]
    [InlineData("Stocked", true)]
    [InlineData("Closed", true)]
    [InlineData("cancelled ", true)]
    public void Finished_statuses(string? status, bool finished)
        => Assert.Equal(finished, WoStatusRules.IsFinished(status));

    [Fact]
    public void Sql_list_matches_the_status_list()
        => Assert.Equal(WoStatusRules.FinishedSqlList, string.Join(",", WoStatusRules.Finished.Select(s => $"'{s}'")));

    [SkippableFact]
    public void Due_date_changes_only_for_open_work_orders()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = TryFactory(); Skip.If(f is null, WriteSkip);
        var repo = new WorkOrderRepository(f!);
        var item = (string)Scalar(f!, "SELECT TOP 1 ItemNo FROM dbo.MD_Item ORDER BY ItemNo;")!;
        var due = DbClock.Today.AddDays(40);
        var oldDue = DbClock.Today.AddDays(20);

        Exec(f!, "DELETE dbo.PP_WorkOrder WHERE CreatedBy = @A;", ("@A", Actor));
        try
        {
            foreach (var st in new[] { "Draft", "Released", "In Progress", "Completed", "Stocked", "Closed", "Cancelled" })
            {
                var woId = (int)Scalar(f!, """
                    INSERT INTO dbo.PP_WorkOrder (WoNumber, ItemNo, OrderQty, OpenQty, RoutingType, Status, DueDate, CreatedBy, CreatedTS)
                    OUTPUT INSERTED.WoID VALUES (@No, @I, 10, 10, 'A', @St, @D, @A, SYSDATETIME());
                    """, ("@No", "ITEST-DUE-" + st.Replace(" ", "")), ("@I", item), ("@St", st), ("@D", oldDue), ("@A", Actor))!;

                var res = repo.UpdateDueDate(woId, due, Actor);
                var saved = (DateTime)Scalar(f!, "SELECT DueDate FROM dbo.PP_WorkOrder WHERE WoID = @W;", ("@W", woId))!;

                if (WoStatusRules.IsFinished(st))
                {
                    Assert.Equal(WorkOrderRepository.DueDateResult.Finished, res);
                    Assert.Equal(oldDue, saved);
                }
                else
                {
                    Assert.Equal(WorkOrderRepository.DueDateResult.Updated, res);
                    Assert.Equal(due, saved);
                }
            }
            Assert.Equal(WorkOrderRepository.DueDateResult.NotFound, repo.UpdateDueDate(-1, due, Actor));
        }
        finally
        {
            Exec(f!, "DELETE dbo.PP_WorkOrder WHERE CreatedBy = @A;", ("@A", Actor));
        }
    }
}
