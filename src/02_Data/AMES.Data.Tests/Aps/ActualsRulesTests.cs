using AMES.Data.Aps;
using Xunit;

namespace AMES.Data.Tests.Aps;

/// <summary>
/// 스펙 §4.4 — 현재 재고를 기준일 아침 재고로 역산. 생산 = SUM(GoodQty)(역분개 −1 포함 순생산) 이라 불량 항이 없다.
/// 완제품은 Used = 0, 사출품은 Shipped = 0 으로 넘어온다. 순수 함수, DB 없음.
/// </summary>
public class ActualsRulesTests
{
    static ActualsRules.Snapshot S(decimal current, decimal tProd, decimal tShip, decimal tUsed,
                                   decimal pProd = 0, decimal pShip = 0, decimal pUsed = 0, string item = "X")
        => new(item, current, tProd, tShip, tUsed, pProd, pShip, pUsed);

    [Fact]
    public void Finished_opening_is_current_minus_today_produced_plus_today_shipped()
    {
        Assert.Equal(90d, ActualsRules.Opening(S(100, 30, 20, 0)));
    }

    [Fact]
    public void Injection_opening_is_current_minus_today_produced_plus_today_used()
    {
        Assert.Equal(34d, ActualsRules.Opening(S(50, 40, 0, 24)));
    }

    [Fact]
    public void Reversal_rows_are_already_in_net_production_and_not_deducted_twice()
    {
        // 기준일 실적 30 EA + 역분개 −1 → SUM(GoodQty) = 29. 불량을 다시 빼는 항이 없어야 한다.
        var withReversal = ActualsRules.Opening(S(100, 29, 20, 0));
        var without      = ActualsRules.Opening(S(100, 30, 20, 0));

        Assert.Equal(91d, withReversal);
        Assert.Equal(1d, withReversal - without);
    }

    [Fact]
    public void Opening_can_go_negative_without_clamping()
    {
        Assert.Equal(-40d, ActualsRules.Opening(S(10, 50, 0, 0)));
    }

    [Fact]
    public void Display_returns_prev_day_values_only_and_keeps_item_no()
    {
        var s = S(100, 30, 20, 5, pProd: 11, pShip: 5, pUsed: 3, item: "83335-P8000RBQ");

        Assert.Equal(new ApsActualInfo("83335-P8000RBQ", 11, 5, 3), ActualsRules.Display(s));
    }

    [Fact]
    public void Opening_uses_decimal_inputs_exactly()
    {
        Assert.Equal(12.5d, ActualsRules.Opening(S(10.25m, 0.25m, 2.5m, 0)));
    }
}
