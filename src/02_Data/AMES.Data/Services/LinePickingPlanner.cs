namespace AMES.Data.Services;

// Planning is read-only; the caller serializes creation and saves requests in its transaction.
internal static class LinePickingPlanner
{
    internal sealed record Step(int WoId, string WoNumber, int Sequence, string LineId, string ItemNo,
        decimal RemainingQty, DateTime? RequiredAt);
    internal sealed record Location(string Id, string LineId, string PartNo, decimal Minimum, decimal Stock,
        string Unit = "EA", bool WrongPart = false);
    internal sealed record Pending(string LocationId, string PartNo, decimal Qty, bool BaseQuantity = true);
    internal sealed record Request(int? WoId, string LineId, string LocationId, string PartNo, string Unit,
        decimal Qty, DateTime? RequiredAt);
    internal sealed record Issue(string WoNumber, string LineId, string PartNo, string Message);
    internal sealed record Result(List<Request> Requests, List<Issue> Issues);

    internal static Result Plan(IReadOnlyList<Step> steps, IReadOnlyList<MrpCalculator.BomLine> bom,
        IReadOnlyList<Location> locations, IReadOnlyList<Pending> pending)
    {
        var result = new Result([], []);
        var children = bom.GroupBy(x => x.ParentItemNo, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var demands = new List<(Step Step, Location Location, decimal Qty)>();
        foreach (var step in steps.Where(s => s.RemainingQty > 0))
        {
            // A prior operation's output is supplied to this operation, not exploded again into its raw materials.
            var priorOutputs = steps.Where(s => s.WoId == step.WoId && s.Sequence < step.Sequence)
                .Select(s => s.ItemNo).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (priorOutputs.Contains(step.ItemNo))
            {
                result.Issues.Add(new(step.WoNumber, step.LineId, step.ItemNo,
                    "This item is shared by multiple operations. Operation-specific material assignment is required."));
                continue;
            }
            var path = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { step.ItemNo };
            var inputs = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            void Walk(string item, decimal qty, int depth)
            {
                if (depth >= 20 || !path.Add(item))
                    throw new InvalidOperationException($"BOM cycle or excessive depth at {item}.");
                var assigned = locations.Any(l => Eq(l.LineId, step.LineId) && Eq(l.PartNo, item));
                if (assigned || priorOutputs.Contains(item) || !children.TryGetValue(item, out var nested))
                    inputs[item] = inputs.GetValueOrDefault(item) + qty;
                else
                    foreach (var child in nested)
                        Walk(child.CompItemNo, qty * Factor(child), depth + 1);
                path.Remove(item);
            }
            try
            {
                if (!children.TryGetValue(step.ItemNo, out var direct))
                {
                    result.Issues.Add(new(step.WoNumber, step.LineId, step.ItemNo, "No effective approved BOM."));
                    continue;
                }
                foreach (var child in direct) Walk(child.CompItemNo, step.RemainingQty * Factor(child), 0);
            }
            catch (InvalidOperationException ex)
            {
                result.Issues.Add(new(step.WoNumber, step.LineId, step.ItemNo, ex.Message));
                continue;
            }
            foreach (var (part, qty) in inputs.Where(x => x.Value > 0))
            {
                var matches = locations.Where(l => Eq(l.LineId, step.LineId) && Eq(l.PartNo, part)).ToList();
                if (matches.Count == 0)
                {
                    result.Issues.Add(new(step.WoNumber, step.LineId, part,
                        "Create an active detail location for this line and check its BOP/BOM."));
                    continue;
                }
                var destination=matches.OrderByDescending(l=>l.Minimum>0).ThenBy(l=>l.Id,StringComparer.OrdinalIgnoreCase).First();
                demands.Add((step, destination, qty));
            }
        }
        foreach (var group in locations.GroupBy(l => (l.LineId.ToUpperInvariant(),l.PartNo.ToUpperInvariant())))
        {
            var members=group.OrderByDescending(l=>l.Minimum>0).ThenBy(l=>l.Id,StringComparer.OrdinalIgnoreCase).ToList();
            var location=members[0];
            var work = demands.Where(d => Eq(d.Location.LineId,location.LineId) && Eq(d.Location.PartNo,location.PartNo)).GroupBy(d => d.Step.WoId)
                .Select(g => (Step: g.First().Step, Qty: g.Sum(d => d.Qty)))
                .OrderBy(d => d.Step.RequiredAt ?? DateTime.MaxValue).ThenBy(d => d.Step.WoId).ToList();
            var open = pending.Where(p => members.Any(l=>Eq(p.LocationId,l.Id)) && Eq(p.PartNo, location.PartNo)).ToList();
            if (open.Any(p => !p.BaseQuantity))
            {
                if (work.Count > 0 || location.Minimum > location.Stock)
                    result.Issues.Add(new(work.FirstOrDefault().Step?.WoNumber ?? "—", location.LineId, location.PartNo,
                        "Resolve the existing BOX-count request before creating quantity-based requests."));
                continue;
            }
            var available = members.Sum(l=>l.Stock) + open.Sum(p => Math.Max(0, p.Qty));
            // Each configured minimum is independent of any WO. BOP-only destinations have a zero floor.
            foreach(var member in members)
            {
                var floor=RoundUp(member.Minimum-member.Stock-open.Where(p=>Eq(p.LocationId,member.Id)).Sum(p=>Math.Max(0,p.Qty)));
                if(floor<=0) continue;
                result.Requests.Add(new(null,member.LineId,member.Id,member.PartNo,member.Unit,floor,null));
                available+=floor;
            }
            foreach (var demand in work)
            {
                var covered = Math.Min(Math.Max(0,available), demand.Qty);
                available -= covered;
                var needed = RoundUp(demand.Qty - covered);
                if (needed > 0)
                    result.Requests.Add(new(demand.Step.WoId, location.LineId, location.Id, location.PartNo,
                        location.Unit, needed, demand.Step.RequiredAt));
            }
        }
        return result;
    }

    private static decimal Factor(MrpCalculator.BomLine line)
    {
        if (line.QtyPer < 0 || line.ScrapPct < 0)
            throw new InvalidOperationException($"Invalid BOM quantity for {line.CompItemNo}.");
        return line.QtyPer * (1 + line.ScrapPct / 100m);
    }
    private static decimal RoundUp(decimal qty) => Math.Max(0, decimal.Ceiling(qty * 1000) / 1000);
    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
