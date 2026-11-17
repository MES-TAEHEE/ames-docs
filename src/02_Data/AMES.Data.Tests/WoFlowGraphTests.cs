using AMES.Contracts.Dto;
using AMES.Data.Repositories;
using AMES.Data.Services;
using Xunit;

namespace AMES.Data.Tests;

// 작업지시 흐름 그래프 — 수주 → 계획 → 작업지시 → 단계, 작업지시 → 슬롯 → 실적
public class WoFlowGraphTests
{
    static readonly WoFlowGraph.Texts T = new("So", "Plan", "Wo", "Step", "Slots", "Result", "Good", "Defect", "slots", "run", "open", "PP-003", "Manual", "None", "Done", "Running", "Waiting", "Cancelled");

    static WorkOrderDto Wo(string? type, string? sref, string status = "Released") => new()
    {
        WoId = 1, WoNumber = "WO-1", ItemNo = "I", ItemName = "n", OrderQty = 10, OpenQty = 10, CompletedQty = 0,
        LineId = "", Status = status, SourceType = type, SourceRef = sref,
    };

    static WorkOrderRepository.WoTraceRow Trace(string? type, string? sref) => new(
        "WO-1", type, sref, "SO-1", 10, "CUS-SAV", 10m, new DateTime(2026, 11, 17), 2, new DateTime(2026, 11, 17), new DateTime(2026, 11, 18), 10m, 4m, 1, 2, "Saved", new DateTime(2026, 11, 16));

    [Fact]
    public void Aps_plan_links_to_its_run_and_every_node_is_connected()
    {
        var steps = new[] { new WorkOrderRepository.StepRow(1, 10, "INJ", "L1", "In Progress", 4m, "I"), new WorkOrderRepository.StepRow(2, 20, "IMG", "L2", "Pending", 0m, "I") };
        var g = WoFlowGraph.Build(Wo("APS", "12"), steps, Trace("APS", "12"), T);
        Assert.Contains(g.Nodes, n => n.Id == "plan" && n.Href == "/pp/aps-plan?run=12");
        Assert.Equal(g.Nodes.Count - 1, g.Edges.Count);                    // 트리 구조 — 노드마다 들어오는 간선 하나
        Assert.All(g.Edges, e => { Assert.Contains(g.Nodes, n => n.Id == e.Source); Assert.Contains(g.Nodes, n => n.Id == e.Target); });
        Assert.True(g.Edges.Single(e => e.Target == "step0").Animated);   // 진행 중 단계로 가는 간선만 움직인다
        Assert.Equal("cur", g.Nodes.Single(n => n.Id == "step0").Status);
        Assert.Equal("Running", g.Nodes.Single(n => n.Id == "step0").StatusLabel);   // 진행 중 단계는 별도 라벨
        Assert.Equal(40d, g.Nodes.Single(n => n.Id == "step0").Progress);           // 4 / 10 EA
        Assert.Equal("Waiting", g.Nodes.Single(n => n.Id == "step1").StatusLabel);
        Assert.Equal(g.Nodes.Count, g.Nodes.Select(n => n.Id).Distinct().Count());
        var toSlots = g.Edges.Single(e => e.Target == "slots");
        Assert.Equal(("b", "t"), (toSlots.SourceHandle, toSlots.TargetHandle));     // 작업지시 아래 → 슬롯 위
    }

    [Fact]
    public void Plan003_and_manual_and_legacy_show_their_own_plan_name_without_a_link()
    {
        Assert.Equal("PP-003", WoFlowGraph.Build(Wo("PLAN003", "PC-1"), [], Trace("PLAN003", "PC-1"), T).Nodes.Single(n => n.Id == "plan").Title);
        Assert.Equal("Manual", WoFlowGraph.Build(Wo("MANUAL", null), [], Trace("MANUAL", null), T).Nodes.Single(n => n.Id == "plan").Title);
        var legacy = WoFlowGraph.Build(Wo(null, null), [], null, T);
        Assert.Equal("None", legacy.Nodes.Single(n => n.Id == "plan").Title);
        Assert.DoesNotContain(legacy.Nodes, n => n.Id is "slots" or "result");  // 추적 정보가 없으면 슬롯·실적 노드는 없다
    }

    [Fact]
    public void Result_node_carries_the_lot_numbers_as_chips()
    {
        var lots = new[] { new WorkOrderRepository.WoLotRow("LOT-A1", "INJ", "CONFIRMED", "I", null), new WorkOrderRepository.WoLotRow("LOT-A2", "INJ", "CONFIRMED", "I", null) };
        var g = WoFlowGraph.Build(Wo("APS", "12"), [], Trace("APS", "12"), T, null, lots);
        var chips = g.Nodes.Single(n => n.Id == "result").Chips!;
        Assert.Equal(["LOT-A1", "LOT-A2"], chips.Where(c => !string.IsNullOrEmpty(c.Key)).Select(c => c.Key));
        Assert.Equal("INJ", chips[0].Text);   // 공정 이름 칩이 앞에 온다
        Assert.Null(WoFlowGraph.Build(Wo("APS", "12"), [], Trace("APS", "12"), T).Nodes.Single(n => n.Id == "result").Chips);   // LOT 이 없으면 칩 없음
    }
}
