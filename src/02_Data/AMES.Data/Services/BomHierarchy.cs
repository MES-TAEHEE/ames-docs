namespace AMES.Data.Services;

/// <summary>
/// BOM 상하위 관계(정본). 품번마다 대표 버전(<see cref="BomVersionRules.Representative"/>) 하나만 관계로 본다 —
/// 부모 P 가 C 를 쓴다 = P 의 대표 버전에 C 가 활성 라인으로 있다. MD-005 BOP 상하위 조회가 쓴다.
/// </summary>
public static class BomHierarchy
{
    public const int DefaultMaxDepth = 9;

    public sealed record Edge(string VersionId, string CompItemNo);

    /// <param name="Depth">음수 = 상위(−1 이 직접 부모), 양수 = 하위(1 이 직접 자식)</param>
    /// <param name="Via">선택 품목과 이 품목 사이의 중간 품목(선택 품목 쪽부터)</param>
    public sealed record Related(string ItemNo, int Depth, IReadOnlyList<string> Via);

    public sealed class Graph
    {
        internal Dictionary<string, List<string>> Children { get; } = new(StringComparer.OrdinalIgnoreCase);
        internal Dictionary<string, List<string>> Parents  { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public static Graph Build(IEnumerable<BomVersionRules.VersionInfo> versions, IEnumerable<Edge> edges, DateOnly today)
    {
        var all  = versions.ToList();
        var reps = all.Where(v => !string.IsNullOrWhiteSpace(v.RootItemNo))
                      .Select(v => v.RootItemNo!.Trim())
                      .Distinct(StringComparer.OrdinalIgnoreCase)
                      .Select(root => BomVersionRules.Representative(all, root, today))
                      .Where(v => v is not null)
                      .ToDictionary(v => v!.VersionId, v => v!.RootItemNo!.Trim(), StringComparer.OrdinalIgnoreCase);

        var g = new Graph();
        foreach (var e in edges)
        {
            if (!reps.TryGetValue(e.VersionId, out var parent) || string.IsNullOrWhiteSpace(e.CompItemNo)) continue;
            var comp = e.CompItemNo.Trim();
            Add(g.Children, parent, comp);
            Add(g.Parents, comp, parent);
        }
        return g;

        static void Add(Dictionary<string, List<string>> map, string key, string value)
        {
            if (!map.TryGetValue(key, out var list)) map[key] = list = new();
            if (!list.Contains(value, StringComparer.OrdinalIgnoreCase)) list.Add(value);
        }
    }

    /// <summary>
    /// 선택 품목의 하위·상위 품목을 너비 우선으로 찾는다 — 품목마다 가장 가까운 경로 하나, 선택 품목 자신 제외,
    /// 순환은 경로에 이미 있는 품목으로 가지 않게 막고 깊이는 <paramref name="maxDepth"/> 까지. 하위에서 찾은 품목은 상위에 다시 넣지 않는다.
    /// </summary>
    public static IReadOnlyList<Related> Find(Graph g, string itemNo, int maxDepth = DefaultMaxDepth)
    {
        var self   = itemNo.Trim();
        var result = new List<Related>();
        var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { self };
        Walk(g.Children, +1);
        Walk(g.Parents, -1);
        return result;

        void Walk(Dictionary<string, List<string>> next, int sign)
        {
            var queue = new Queue<(string Item, int Depth, List<string> Path)>();
            queue.Enqueue((self, 0, new List<string>()));
            while (queue.Count > 0)
            {
                var (item, depth, path) = queue.Dequeue();
                if (depth >= maxDepth || !next.TryGetValue(item, out var list)) continue;
                foreach (var n in list)
                {
                    if (string.Equals(n, self, StringComparison.OrdinalIgnoreCase) || path.Contains(n, StringComparer.OrdinalIgnoreCase)) continue;
                    if (!seen.Add(n)) continue;
                    result.Add(new Related(n, sign * (depth + 1), path.ToList()));
                    queue.Enqueue((n, depth + 1, new List<string>(path) { n }));
                }
            }
        }
    }
}
