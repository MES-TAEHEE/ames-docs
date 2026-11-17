using AMES.Data.Connection;
using AMES.Data.Repositories;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

/// <summary>
/// PP-003 검토 대기(보류) — 보류한 수주는 기본 목록에서 빠지고 「검토 대기 보기」에만 나오며, 풀면 돌아온다.
/// 수량·납기가 바뀌어 다시 들어오면(업서트) 자동으로 풀린다. 쓰기 테스트라 AMES_TEST_CONN 을 지정했을 때만 돈다.
/// </summary>
[Collection("SoAutoConfirm")]
public class PlanHoldTests
{
    const string WriteSkip = "set AMES_TEST_CONN to run — writes PP_CustomerOrder (test customer rows are removed)";
    const string Actor = "itest-hold";
    const string Cust = "ITEST-HOLD";

    static AmesConnectionFactory? WriteFactory()
        => Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null ? null : TryFactory();

    static void Clean(AmesConnectionFactory f) => Exec(f, "DELETE dbo.PP_CustomerOrder WHERE CustomerID = @C", ("@C", Cust));

    static PpRepository.CustomerOrderImportRow Row(decimal qty = 10m, DateTime? due = null) =>
        new("ITEST-HOLD-SO", 10, "ITEST-ITEM", qty, 0m, new DateTime(2026, 10, 1), due ?? new DateTime(2026, 10, 20));

    [SkippableFact]
    public void Held_order_leaves_the_default_list_shows_in_the_held_list_and_comes_back_when_released()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        var pp = new PpRepository(f!);
        Clean(f!);
        try
        {
            pp.UpsertCustomerOrders(Cust, [Row()], Actor);
            Exec(f!, "UPDATE dbo.PP_CustomerOrder SET Status = 'Confirmed' WHERE CustomerID = @C", ("@C", Cust));
            var soId = (int)Scalar(f!, "SELECT SoID FROM dbo.PP_CustomerOrder WHERE CustomerID = @C", ("@C", Cust))!;

            Assert.Contains(pp.ListPlanCandidates(Cust, null, null), r => r.SoId == soId);

            Assert.Equal(1, pp.SetPlanHold([soId], true, "고객 확인 대기", Actor));
            Assert.DoesNotContain(pp.ListPlanCandidates(Cust, null, null), r => r.SoId == soId);
            var held = pp.ListPlanCandidates(Cust, null, null, onlyHeld: true).Single(r => r.SoId == soId);
            Assert.True(held.PlanHold);
            Assert.Equal("고객 확인 대기", held.PlanHoldReason);
            Assert.Equal(1, pp.CountPlanHeld(Cust, null, null));

            Assert.Equal(1, pp.SetPlanHold([soId], false, null, Actor));
            Assert.Contains(pp.ListPlanCandidates(Cust, null, null), r => r.SoId == soId);
            Assert.Equal(0, pp.CountPlanHeld(Cust, null, null));
        }
        finally { Clean(f!); }
    }

    [SkippableFact]
    public void Upload_with_changed_quantity_or_due_date_releases_the_hold_but_an_unchanged_one_keeps_it()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        var pp = new PpRepository(f!);
        Clean(f!);
        try
        {
            pp.UpsertCustomerOrders(Cust, [Row()], Actor);
            Exec(f!, "UPDATE dbo.PP_CustomerOrder SET Status = 'Confirmed' WHERE CustomerID = @C", ("@C", Cust));
            var soId = (int)Scalar(f!, "SELECT SoID FROM dbo.PP_CustomerOrder WHERE CustomerID = @C", ("@C", Cust))!;
            pp.SetPlanHold([soId], true, "r", Actor);

            pp.UpsertCustomerOrders(Cust, [Row()], Actor);                       // 같은 값으로 다시 들어옴 — 보류 유지
            Assert.True(Convert.ToBoolean(Scalar(f!, "SELECT PlanHold FROM dbo.PP_CustomerOrder WHERE SoID = @I", ("@I", soId))));

            pp.UpsertCustomerOrders(Cust, [Row(qty: 25m)], Actor);               // 수량이 바뀌어 들어옴 — 자동 해제
            Assert.False(Convert.ToBoolean(Scalar(f!, "SELECT PlanHold FROM dbo.PP_CustomerOrder WHERE SoID = @I", ("@I", soId))));

            pp.SetPlanHold([soId], true, "r", Actor);
            pp.UpsertCustomerOrders(Cust, [Row(qty: 25m, due: new DateTime(2026, 10, 27))], Actor);   // 납기가 바뀌어 들어옴 — 자동 해제
            Assert.False(Convert.ToBoolean(Scalar(f!, "SELECT PlanHold FROM dbo.PP_CustomerOrder WHERE SoID = @I", ("@I", soId))));
        }
        finally { Clean(f!); }
    }

    [SkippableFact]
    public void Only_confirmed_orders_can_be_held()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        var pp = new PpRepository(f!);
        Clean(f!);
        try
        {
            pp.UpsertCustomerOrders(Cust, [Row()], Actor);
            Exec(f!, "UPDATE dbo.PP_CustomerOrder SET Status = 'Open' WHERE CustomerID = @C", ("@C", Cust));
            var soId = (int)Scalar(f!, "SELECT SoID FROM dbo.PP_CustomerOrder WHERE CustomerID = @C", ("@C", Cust))!;
            Assert.Equal(0, pp.SetPlanHold([soId], true, null, Actor));
        }
        finally { Clean(f!); }
    }
}
