using System.Text.Json;
using AMES.Data.Aps;
using AMES.Data.Aps.Contracts;
using AMES.Data.Repositories;
using Xunit;

namespace AMES.Data.Tests.Aps;

/// <summary>
/// PlanBundle + PlanResult → PP_ApsPlanLine 정규화 사본(스펙 §3.4·§7). 골든 픽스처 plan_LQ10.json(완제품 60 × 사출 36 × 날짜 5)으로 고정. 순수 함수, DB 없음.
/// </summary>
public class ApsPlanLinesTests
{
    static PlanResponse Load() =>
        JsonSerializer.Deserialize<PlanResponse>(File.ReadAllText(Fixtures.Path("plan_LQ10.json")), ApsJson.Options)!;

    static readonly DateOnly Sep24 = new(2026, 9, 24);

    [Fact]
    public void Rows_are_every_row_times_every_date()
    {
        var p = Load();

        var rows = ApsPlanLines.From(p.Bundle, p.Result);

        Assert.Equal(60 * 5 + 36 * 5, rows.Count);
        Assert.Equal(300, rows.Count(r => r.Kind == ApsRepository.KindAsm));
        Assert.Equal(180, rows.Count(r => r.Kind == ApsRepository.KindInj));
        Assert.All(rows, r => { Assert.Equal(0, r.PlanLineId); Assert.Equal(0, r.RunId); Assert.Null(r.WoId); });
    }

    [Fact]
    public void Assembly_row_carries_bundle_day_and_result_cell()
    {
        var p = Load();

        var r = ApsPlanLines.From(p.Bundle, p.Result).Single(x => x.Kind == ApsRepository.KindAsm && x.ItemNo == "82301-P8140LBF" && x.PlanDate == Sep24);

        Assert.Equal("LQ10", r.LineId);
        Assert.Equal(15m, r.Demand);
        Assert.Equal(11m, r.Supply);
        Assert.Equal(17m, r.Stock);
        Assert.Equal("ok", r.Status);
        Assert.Equal((0m, 0m, 0m), (r.Requirement, r.PlanDay, r.PlanNight));
        Assert.False(r.Locked);
    }

    [Fact]
    public void Injection_row_carries_requirement_plan_and_stock_from_result()
    {
        var p = Load();

        var r = ApsPlanLines.From(p.Bundle, p.Result).Single(x => x.Kind == ApsRepository.KindInj && x.ItemNo == "82310-P8000D4P" && x.PlanDate == Sep24);

        Assert.Equal("M2A100", r.LineId);
        Assert.Equal(30m, r.Requirement);
        Assert.Equal(187m, r.Stock);
        Assert.Equal((0m, 0m, 0m, 0m), (r.Demand, r.Supply, r.PlanDay, r.PlanNight));
        Assert.Equal("ok", r.Status);
    }

    [Fact]
    public void Locked_plan_and_status_pass_through()
    {
        var p = Load();
        p.Bundle.Assembly[0].Days[0].Locked = true;
        p.Bundle.Injection[0].Days[1].PlanDay = 36;
        p.Bundle.Injection[0].Days[1].PlanNight = 12;
        p.Bundle.Injection[0].Days[1].Locked = true;

        var rows = ApsPlanLines.From(p.Bundle, p.Result);

        var asm = rows.Single(r => r.Kind == ApsRepository.KindAsm && r.ItemNo == p.Bundle.Assembly[0].PartNo && r.PlanDate == Sep24);
        Assert.True(asm.Locked);
        var inj = rows.Single(r => r.Kind == ApsRepository.KindInj && r.ItemNo == p.Bundle.Injection[0].PartNo && r.PlanDate == new DateOnly(2026, 9, 25));
        Assert.Equal((36m, 12m, true), (inj.PlanDay, inj.PlanNight, inj.Locked));
        Assert.Contains(rows, r => r.Kind == ApsRepository.KindAsm && r.Status == "short");   // 픽스처에 부족 셀이 있다
    }

    [Fact]
    public void Missing_result_cell_defaults_to_zero_stock_and_ok()
    {
        var p = Load();

        var rows = ApsPlanLines.From(p.Bundle, new PlanResult());

        Assert.Equal(480, rows.Count);
        Assert.All(rows, r => { Assert.Equal(0m, r.Stock); Assert.Equal("ok", r.Status); });
        Assert.Equal(15m, rows.Single(r => r.Kind == ApsRepository.KindAsm && r.ItemNo == "82301-P8140LBF" && r.PlanDate == Sep24).Demand);
    }

    [Fact]
    public void Quantities_round_to_three_decimals_and_line_falls_back_to_bundle_line()
    {
        var p = Load();
        p.Bundle.Assembly[0].Days[0].Demand = 1.23456;
        p.Bundle.Assembly[0].LineCd = null;

        var r = ApsPlanLines.From(p.Bundle, p.Result).First(x => x.Kind == ApsRepository.KindAsm && x.ItemNo == p.Bundle.Assembly[0].PartNo);

        Assert.Equal(1.235m, r.Demand);
        Assert.Equal(p.Bundle.Line.LineCd, r.LineId);
    }

    [Fact]
    public void Injection_row_same_item_follows_self_edge()
    {
        var p = Load();
        var rows = ApsPlanLines.From(p.Bundle, p.Result);
        Assert.All(rows, r => Assert.False(r.SameItem));            // plan_LQ10.json 의 BOM 140 간선은 전부 부모 ≠ 자식

        var x = p.Bundle.Injection[0].PartNo;
        p.Bundle.Bom.Add(new BomEdge(x, x, 1));                      // 같은 품번 규칙(스펙 §4.2 ①) = 자기 간선

        rows = ApsPlanLines.From(p.Bundle, p.Result);

        Assert.All(rows.Where(r => r.Kind == ApsRepository.KindInj && r.ItemNo == x), r => Assert.True(r.SameItem));
        Assert.All(rows.Where(r => r.Kind == ApsRepository.KindAsm || r.ItemNo != x), r => Assert.False(r.SameItem));
    }
}
