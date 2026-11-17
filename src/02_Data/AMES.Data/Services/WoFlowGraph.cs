using AMES.Contracts.Dto;
using AMES.Data.Repositories;

namespace AMES.Data.Services;

/// <summary>
/// 작업지시 팝업 「흐름」 보기의 그래프 — 수주 → 계획 → 작업지시 → (공정 단계 …) 와 작업지시 → 라인 슬롯 → 실적.
/// 화면(React Flow)이 그대로 그리도록 좌표·색 상태까지 정한다. 순수 함수라 테스트한다(WoFlowGraphTests).
/// </summary>
public static class WoFlowGraph
{
    public sealed record Node(string Id, string Kind, string KindLabel, string Title, IReadOnlyList<string> Lines,
        string? Status, string? Href, string? LinkText, int X, int Y, string? StatusLabel = null, double? Progress = null, IReadOnlyList<OperatorRow>? OperatorRows = null, IReadOnlyList<Chip>? Chips = null);
    /// <summary>노드 안의 눌러 볼 수 있는 칩 — 흐름 보기의 LOT 번호(Key = LOT 번호). 누르면 화면이 LOT 팝업을 연다.</summary>
    public sealed record Chip(string Text, string Key);
    /// <summary>공정 담당자 한 줄 — 담당자마다 한 줄씩 보인다. Live = 지금 작업 중(강조).</summary>
    public sealed record OperatorRow(string Text, bool Live);
    public sealed record Edge(string Id, string Source, string Target, bool Animated, string? Label, string? SourceHandle = null, string? TargetHandle = null);
    public sealed record Graph(IReadOnlyList<Node> Nodes, IReadOnlyList<Edge> Edges);

    /// <summary>화면에 보이는 말(번역된 문구). 키가 아니라 값을 받아 순수 함수로 둔다.</summary>
    public sealed record Texts(string So, string Plan, string Wo, string Step, string Slots, string Result,
        string Good, string Defect, string SlotUnit, string Run, string OpenLink, string SourcePlan003, string SourceManual, string SourceNone,
        string Done = "", string Running = "", string Waiting = "", string Cancelled = "");

    // 노드 폭 260 + 진행 중 확대(1.12배) 여유 + 간격 — 공정 노드끼리 겹치지 않게 열 간격은 노드 폭보다 충분히 크게
    // const int ColW = 600, RowH = 400;
    // const int ColW = 560, RowH = 360;   // 공정 노드 폭 460(담당자 줄이 한 줄에 들어가는 폭) + 간격
    const int ColW = 480, RowH = 360;   // 공정 노드 폭 460(담당자 줄이 한 줄에 들어가는 폭) + 간격

    static string Label(string status, Texts x) => status switch { "done" => x.Done, "cur" => x.Running, "bad" => x.Cancelled, _ => x.Waiting };
    static double Pct(decimal done, decimal total) => total <= 0 ? 0 : (double)Math.Min(100m, done * 100m / total);

    public static Graph Build(WorkOrderDto wo, IReadOnlyList<WorkOrderRepository.StepRow> steps, WorkOrderRepository.WoTraceRow? t, Texts x,
        IReadOnlyDictionary<string, IReadOnlyList<OperatorRow>>? operators = null, IReadOnlyList<WorkOrderRepository.WoLotRow>? lots = null, IReadOnlyList<WorkOrderRepository.WoStepTrace>? stepTrace = null)
    {
        var nodes = new List<Node>();
        var edges = new List<Edge>();
        var woStatus = wo.Status == "Cancelled" ? "bad" : wo.Status is "Closed" or "Completed" ? "done" : wo.Status == "In Progress" ? "cur" : "todo";

        // 수주
        var soText = t?.SoNumber is null ? "—" : t.SoNumber + (t.SoLineNo is int ln ? "-" + ln : "");
        nodes.Add(new Node("so", "so", x.So, soText,
            [$"{t?.CustomerId ?? "—"} · {(t?.SoQty is decimal q ? q.ToString("#,0") + " EA" : "—")}", t?.SoDue is DateTime d ? d.ToString("yyyy-MM-dd") : "—"],
            null, null, null, 0, 0));

        // 계획 — APS 는 실행 화면으로 이어진다
        var plan = PlanNode(wo, t, x);
        nodes.Add(plan);
        edges.Add(new Edge("so-plan", "so", "plan", false, null));

        // 작업지시
        nodes.Add(new Node("wo", "wo", x.Wo, wo.WoNumber,
            [$"{wo.ItemNo}", $"{wo.OrderQty:#,0} EA · {wo.CompletedQty:#,0} ({(int)wo.ProgressPct}%)", wo.Status],
            woStatus, null, null, 2 * ColW, 0, Label(woStatus, x), Pct(wo.CompletedQty, wo.OrderQty)));
        edges.Add(new Edge("plan-wo", "plan", "wo", false, null));

        // 공정 단계 — 작업지시에서 이어서 한 줄로
        var prev = "wo";
        for (var i = 0; i < steps.Count; i++)
        {
            var s = steps[i];
            var id = "step" + i;
            var st = s.Status == "Closed" ? "done" : s.Status == "In Progress" ? "cur" : "todo";
            IReadOnlyList<OperatorRow>? rows = null;
            var hasOp = s.LineId is not null && operators is not null && operators.TryGetValue(s.LineId, out rows);
            nodes.Add(new Node(id, "step", $"{x.Step} {s.StepSeq}", s.ProcessCode,
                [s.LineId ?? "—", $"{s.CompletedQty:#,0} / {wo.OrderQty:#,0} EA", s.Status],
                st, null, null, (3 + i) * ColW, 0, Label(st, x), Pct(s.CompletedQty, wo.OrderQty),
                hasOp ? rows : null));
            edges.Add(new Edge($"{prev}-{id}", prev, id, st == "cur", null));
            prev = id;
        }

        // 라인 슬롯 → 실적 — 작업지시 아래
        if (t is not null)
        {
            var span = t.SlotFrom is DateTime a ? a.ToString("yyyy-MM-dd") + (t.SlotTo is DateTime b && b != a ? " ~ " + b.ToString("yyyy-MM-dd") : "") : "—";
            // 슬롯·실적은 공정 단계별로 보인다 — 합계는 INJ 와 IMG 를 더해 같은 제품이 두 번 세어지기 때문(주문 10 인데 20)
            var stepLines = stepTrace is { Count: > 0 };
            var slotLines = stepLines
                ? stepTrace!.Where(s => s.SlotCount > 0).Select(s => $"{s.ProcessCode} {s.LineId} · {s.SlotQty:#,0} EA").Append(span).ToList()
                : new List<string> { $"{t.SlotQty:#,0} EA", span };
            nodes.Add(new Node("slots", "slot", x.Slots, $"{t.SlotCount} {x.SlotUnit}", slotLines, null, "/pp/line-schedule", x.OpenLink, 2 * ColW, RowH));
            // 작업지시 아래(b)에서 나와 슬롯 위(t)로 들어간다 — 슬롯이 작업지시 바로 아래 줄에 놓인다
            edges.Add(new Edge("wo-slots", "wo", "slots", false, null, "b", "t"));
            // 실적 — 제목은 마지막 공정(작업지시 완료 수량과 같은 기준), 줄은 공정별 양품·불량·LOT
            var finalGood = stepLines ? stepTrace!.OrderBy(s => s.StepSeq).Last().GoodQty : t.GoodQty;
            var resultLines = stepLines
                ? stepTrace!.Select(s => $"{s.ProcessCode} · {x.Good} {s.GoodQty:#,0} · {x.Defect} {s.DefectCount} · LOT {s.LotCount}").ToList()
                : new List<string> { $"{x.Defect} {t.DefectCount}", $"LOT {Math.Max(t.LotCount, lots?.Count ?? 0)}" };
            // LOT 칩은 공정마다 묶어 앞에 공정 이름 칩(Key 없음)을 둔다
            List<Chip>? chips = null;
            if (lots is { Count: > 0 })
            {
                chips = new List<Chip>();
                foreach (var g in lots.GroupBy(l => l.ProcessCode ?? ""))
                {
                    if (!string.IsNullOrEmpty(g.Key)) chips.Add(new Chip(g.Key, ""));
                    chips.AddRange(g.Select(l => new Chip(l.LotCode, l.LotCode)));
                }
            }
            nodes.Add(new Node("result", "result", x.Result, $"{x.Good} {finalGood:#,0}", resultLines, finalGood > 0 ? "done" : null, null, null, 3 * ColW, RowH, Chips: chips));
            edges.Add(new Edge("slots-result", "slots", "result", false, null));
        }
        return new Graph(nodes, edges);
    }

    static Node PlanNode(WorkOrderDto wo, WorkOrderRepository.WoTraceRow? t, Texts x)
    {
        if (wo.SourceType == "APS" && int.TryParse(wo.SourceRef, out var run))
            return new Node("plan", "plan", x.Plan, $"APS {x.Run} #{run}", [t?.ApsStatus ?? "—", t?.ApsAt is DateTime d ? d.ToString("yyyy-MM-dd HH:mm") : "—"],
                null, $"/pp/aps-plan?run={run}", x.OpenLink, ColW, 0);
        var name = wo.SourceType switch { "PLAN003" => x.SourcePlan003, "MANUAL" => x.SourceManual, _ => x.SourceNone };
        return new Node("plan", "plan", x.Plan, name, [wo.SourceRef ?? "—"], null, null, null, ColW, 0);
    }
}
