using AMES.Data.Aps.Contracts;

namespace AMES.Data.Aps.Domain;

/// 공정 전개(POST /api/chain/plan). 규칙은 원본 chain_LQ10.json 231행으로 확정 (tools/rules-lab/chain_lab.py):
///   · BFS: 루트(요청 순) → 자식(품번순), 처음 본 레벨에 둔다. 루트는 행이 되지 않는다. levels 까지.
///   · 행이 되는 조건: 소요(또는 첫날 이전 몫)가 하나라도 0 이 아니어야 한다. 계획이 0 인 행의 자식 간선도 edges 에는 센다(원본 526 = 518 + 8).
///   · gap = offset(자식 라인) − offset(부모 라인). requirement[i] = Σ 부모 plan[i+gap] × qtyPer, overdue = 부모 plan 중 창 앞으로 밀린 몫.
///   · usesStock: cover = CoverFor(소요 > 0 인 날의 평균), target = round(cover × 다음 3일 최대 소요), plan = 올림_pack(req + target − opening).
///     batchDays > 1: target 0, opening < req 인 날에만 다음 batchDays 일 소요 합 − opening. usesStock=false: plan = req.
///   · pegs: 부모 plan 을 그날 부모 소요 중 루트 몫 비율로 나눠 내려온다(재고·배치가 반영된 값). total 내림차순, 0 은 뺀다.
///   · loads: 라인·날짜 qty = Σ plan, hours = Σ round2(plan/uph), capacity = 사출기면 교대 합 아니면 0.
public static class ChainExpander
{
    public static ChainResponse Expand(ChainRequest req, IReadOnlyList<BomEdge> edges, IReadOnlyDictionary<string, PartInfo> parts,
                                       IReadOnlyDictionary<string, LineInfo> lines, IReadOnlyDictionary<string, OpeningInfo> opening,
                                       ShiftRules rules, StageRules stages)
    {
        var n = req.Dates.Count;
        var rootLine = req.Roots.Select(r => r.LineCd).FirstOrDefault(l => !string.IsNullOrEmpty(l)) ?? "";
        var rootSet = req.Roots.Select(r => r.PartNo).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var childrenOf = edges.Where(e => parts.ContainsKey(e.ChildPartNo))
            .GroupBy(e => e.ParentPartNo, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(e => e.ChildPartNo, StringComparer.Ordinal).ToList(), StringComparer.OrdinalIgnoreCase);

        // 노드: 루트 + BFS 로 찾은 자식. plan/req/pegs 는 계산 순서(부모 먼저)대로 채운다.
        var nodes = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
        var order = new List<Node>();
        foreach (var r in req.Roots)
        {
            if (nodes.ContainsKey(r.PartNo)) continue;
            var plan = req.Dates.Select(d => r.Plan.GetValueOrDefault(d)).ToArray();
            var node = new Node(r.PartNo, r.LineCd ?? "", 0) { Plan = plan, Req = plan, IsRoot = true };
            node.Pegs[r.PartNo] = plan;
            nodes[r.PartNo] = node; order.Add(node);
        }
        var frontier = order.ToList();
        for (var level = 1; level <= req.Levels && frontier.Count > 0; level++)
        {
            var next = new List<Node>();
            foreach (var parent in frontier)
            {
                if (!childrenOf.TryGetValue(parent.PartNo, out var kids)) continue;
                foreach (var e in kids)
                {
                    if (rootSet.Contains(e.ChildPartNo)) { parent.EdgesOut++; continue; }   // 루트로 되돌아가는 간선은 세기만
                    parent.EdgesOut++;
                    if (!nodes.TryGetValue(e.ChildPartNo, out var child))
                    {
                        var pi = parts[e.ChildPartNo];
                        child = new Node(e.ChildPartNo, pi.LineCd ?? "", level) { PartName = pi.PartName, PartUph = pi.Uph };
                        nodes[e.ChildPartNo] = child; order.Add(child); next.Add(child);
                    }
                    child.ParentEdges.Add((parent, e.QtyPer));
                }
            }
            frontier = next;
        }

        // 부모가 모두 계산된 노드부터 (같은 레벨 부모도 있을 수 있다)
        var done = new HashSet<string>(rootSet, StringComparer.OrdinalIgnoreCase);
        var pending = order.Where(x => !x.IsRoot).ToList();
        while (pending.Count > 0)
        {
            var progressed = false;
            foreach (var node in pending.ToList())
            {
                if (node.ParentEdges.Any(pe => !done.Contains(pe.parent.PartNo))) continue;
                Compute(node, n, req, lines, opening, rules, stages, rootLine);
                done.Add(node.PartNo); pending.Remove(node); progressed = true;
            }
            if (!progressed)   // 순환: 남은 것은 계산된 부모만으로 간다
                foreach (var node in pending.ToList()) { Compute(node, n, req, lines, opening, rules, stages, rootLine, ignoreUndone: done); done.Add(node.PartNo); pending.Remove(node); }
        }

        var res = new ChainResponse { BaseDate = req.BaseDate, Dates = req.Dates.ToList(), Roots = req.Roots.Count };
        var rows = order.Where(x => !x.IsRoot && (x.Req.Any(v => v != 0) || x.Overdue > 0)).ToList();
        var rowSet = rows.Select(x => x.PartNo).ToHashSet(StringComparer.OrdinalIgnoreCase);
        res.Edges = order.Where(x => x.IsRoot || rowSet.Contains(x.PartNo)).Sum(x => x.EdgesOut);
        foreach (var g in rows.GroupBy(x => x.Level).OrderBy(g => g.Key))
        {
            var lv = new ChainLevel { Level = g.Key };
            foreach (var node in g.OrderBy(x => x.LineCd, StringComparer.Ordinal).ThenBy(x => x.PartNo, StringComparer.Ordinal)) lv.Rows.Add(node.Row!);
            lv.Lines = g.Select(x => x.LineCd).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal).ToList();
            res.Levels.Add(lv);
        }

        // 라인 × 날짜 부하 (계획 0 인 칸은 넣지 않는다)
        var loads = new Dictionary<(string line, string date), (double qty, double hours)>();
        foreach (var node in rows)
            for (var i = 0; i < n; i++)
            {
                var q = node.Plan[i]; if (q <= 0) continue;
                var key = (node.LineCd, req.Dates[i]); var cur = loads.GetValueOrDefault(key);
                loads[key] = (cur.qty + q, cur.hours + (node.Uph > 0 ? Math.Round(q / node.Uph, 2, MidpointRounding.AwayFromZero) : 0));
            }
        foreach (var ((line, date), (qty, hours)) in loads.OrderBy(k => k.Key.line, StringComparer.Ordinal).ThenBy(k => k.Key.date, StringComparer.Ordinal))
        {
            var inj = lines.TryGetValue(line, out var li) && li.Type == "injection";
            var sh = rules.ShiftFor(line, date, inj);
            var cap = sh.src == "없음" ? 0 : sh.day + sh.night;
            var h = Math.Round(hours, 2, MidpointRounding.AwayFromZero);
            res.Loads.Add(new ChainLoad { LineCd = line, Date = date, Qty = qty, Hours = h, Capacity = cap, Over = cap > 0 && h > cap + 1e-9 });
        }
        var over = res.Loads.Count(l => l.Over);
        var overdue = rows.Count(x => x.Overdue > 0);
        if (over > 0) res.Warnings.Add($"하루 가동시간을 넘는 라인·날짜가 {over}건 있습니다. 앞당기거나 특근이 필요합니다.");
        if (overdue > 0) res.Warnings.Add($"품번 {overdue}건은 선행일 때문에 계획 첫날 이전에 만들어져 있어야 했던 몫이 있습니다. 지난 일이라 계획에는 넣지 않았습니다 (첫날 소요 칸에 표시).");
        return res;
    }

    static void Compute(Node node, int n, ChainRequest req, IReadOnlyDictionary<string, LineInfo> lines, IReadOnlyDictionary<string, OpeningInfo> opening,
                        ShiftRules rules, StageRules stages, string rootLine, HashSet<string>? ignoreUndone = null)
    {
        var reqs = new double[n]; double overdue = 0;
        var pegs = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
        var parentLines = new List<string>();
        var myOff = stages.OffsetDays(node.LineCd, rootLine);
        foreach (var (parent, qtyPer) in node.ParentEdges)
        {
            if (ignoreUndone != null && !ignoreUndone.Contains(parent.PartNo)) continue;
            var gap = myOff - (parent.IsRoot ? 0 : stages.OffsetDays(parent.LineCd, rootLine));
            if (!parentLines.Contains(parent.LineCd, StringComparer.OrdinalIgnoreCase)) parentLines.Add(parent.LineCd);
            for (var i = 0; i < n; i++)
            {
                var j = i + gap; if (j < 0 || j >= n) continue;
                var pq = parent.Plan[j] * qtyPer;
                reqs[i] += pq;
                if (pq == 0 || parent.Req[j] <= 0) continue;
                foreach (var (root, rootDays) in parent.Pegs)
                {
                    var share = rootDays[j] / parent.Req[j];                  // 그날 부모 소요 중 이 루트의 몫
                    if (share == 0) continue;
                    if (!pegs.TryGetValue(root, out var arr)) pegs[root] = arr = new double[n];
                    arr[i] += pq * share;
                }
            }
            for (var j = 0; j < Math.Min(gap, n); j++) overdue += parent.Plan[j] * qtyPer;
        }
        node.Req = reqs; node.Overdue = overdue; node.Pegs = pegs;

        // UPH: 사출 능력 규칙 → 품번 UPH(ACD0021.ZUPH) → 사출기는 라인 마스터 UPH, 조립·반제품 라인은 60 (원본 chain: LA20·LG20·LU20 행 60)
        var rule = rules.UphRuleFor(node.PartNo);
        var isInjection = lines.TryGetValue(node.LineCd, out var li) && li.Type == "injection";
        node.Uph = rule?.Uph ?? (node.PartUph > 0 ? node.PartUph : isInjection ? li!.Uph : 60);
        var pack = rule?.PackSize ?? rules.PackFor(node.PartNo);
        var usesStock = stages.UsesStock(node.LineCd, rootLine);
        var batch = stages.BatchDays(node.LineCd, rootLine);
        var oi = opening.GetValueOrDefault(node.PartNo);
        var plan = new double[n];
        var days = new List<ChainDay>();
        if (!usesStock)
        {
            for (var i = 0; i < n; i++) { plan[i] = reqs[i]; days.Add(new ChainDay { Date = req.Dates[i], Requirement = reqs[i], Plan = reqs[i], Overdue = i == 0 ? overdue : 0 }); }
        }
        else
        {
            var nz = reqs.Where(v => v > 0).ToList();
            var cover = rules.CoverFor(nz.Count > 0 ? nz.Average() : 0);
            var open = oi?.Qty ?? 0;
            for (var i = 0; i < n; i++)
            {
                double target, q;
                if (batch > 1)
                {
                    target = 0;
                    q = open < reqs[i] ? ShiftRules.RoundUp(reqs.Skip(i).Take(batch).Sum() - open, pack) : 0;
                }
                else
                {
                    var fut = reqs.Skip(i + 1).Take(3).ToList();
                    target = i == n - 1 || fut.Count == 0 ? 0 : Math.Round(cover * fut.Max(), MidpointRounding.AwayFromZero);
                    var need = reqs[i] + target - open;
                    q = need > 0 ? ShiftRules.RoundUp(need, pack) : 0;
                }
                var closing = open + q - reqs[i];
                days.Add(new ChainDay { Date = req.Dates[i], Requirement = reqs[i], Opening = open, Target = target, Plan = q, Closing = closing, Overdue = i == 0 ? overdue : 0, Shortage = Math.Min(0, closing) });
                plan[i] = q; open = closing;
            }
        }
        node.Plan = plan;

        var pegList = pegs.Select(kv => new Peg { PartNo = kv.Key, PartName = req.Roots.FirstOrDefault(r => string.Equals(r.PartNo, kv.Key, StringComparison.OrdinalIgnoreCase))?.PartName, Days = kv.Value.ToList(), Total = kv.Value.Sum() })
            .Where(p => p.Total > 0).OrderByDescending(p => p.Total).ToList();
        var leads = new List<string>();
        foreach (var pl in parentLines)
        {
            var gap = myOff - (string.Equals(pl, rootLine, StringComparison.OrdinalIgnoreCase) ? 0 : stages.OffsetDays(pl, rootLine));
            if (gap > 0) leads.Add($"{pl} −{gap}일");
        }
        node.Row = new ChainRow
        {
            PartNo = node.PartNo, PartName = node.PartName, LineCd = node.LineCd, Level = node.Level,
            OpeningStock = usesStock ? oi?.Qty ?? 0 : 0, StockCounted = usesStock && (oi?.Counted ?? false), UsesStock = usesStock, PackSize = pack, BatchDays = batch, Uph = node.Uph,
            Parents = node.ParentEdges.Select(pe => pe.parent.PartNo).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal).ToList(),
            Leads = leads, Days = days, Pegs = pegList, PegCount = pegList.Count, PegOthers = new(), ShortDays = days.Count(d => d.Shortage < 0),
        };
    }

    sealed class Node(string partNo, string lineCd, int level)
    {
        public string PartNo { get; } = partNo; public string LineCd { get; } = lineCd; public int Level { get; } = level;
        public string? PartName; public bool IsRoot; public double Uph; public double PartUph; public double Overdue; public int EdgesOut;
        public double[] Plan = Array.Empty<double>(); public double[] Req = Array.Empty<double>();
        public Dictionary<string, double[]> Pegs = new(StringComparer.OrdinalIgnoreCase);
        public List<(Node parent, double qtyPer)> ParentEdges = new();
        public ChainRow? Row;
    }
}
