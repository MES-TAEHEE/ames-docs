using Microsoft.Data.SqlClient;

namespace AMES.Data.Services;

/// <summary>
/// WO 품번 → 코어(사출) 품번. 유효 BOM 을 SUB 자식으로만 내려가며 품명이 CORE 로 시작하는 SUB 를 찾는다.
/// 정확히 1개 = Core, SUB 하위가 아예 없으면 Self(단계 품번 = WO 품번), 있는데 0개 = Missing, 2개 이상 = Ambiguous.
/// 품명 규칙은 사용자 결정(접근 B) — WO 발행 시점에 한 번 해석해 PP_WorkOrderRouting.ItemNo 에 스냅샷하므로
/// 런타임 조회는 이 규칙을 다시 타지 않는다. 정본 테스트: CoreItemResolverTests / CoreItemDbTests.
/// </summary>
public static class CoreItemResolver
{
    /// <summary>코어를 만드는 공정. MoldResolver.NeedsMold 도 이 상수를 본다.</summary>
    public const string CoreProcess = "INJ";
    const int MaxDepth = 8;

    public static bool IsCoreProcess(string? processCode) =>
        string.Equals(processCode, CoreProcess, StringComparison.OrdinalIgnoreCase);

    public static bool IsCoreName(string? itemName) =>
        itemName is not null && itemName.TrimStart().StartsWith("CORE", StringComparison.OrdinalIgnoreCase);

    public enum Outcome { Self, Core, Missing, Ambiguous }

    public sealed record BomEdge(string ParentItemNo, string CompItemNo, string? CompItemType, string? CompItemName);

    public sealed record Result(Outcome Outcome, string? CoreItemNo)
    {
        public bool NoCore => Outcome is Outcome.Missing or Outcome.Ambiguous;
        public string StepItemNo(string woItemNo) => Outcome == Outcome.Core ? CoreItemNo! : woItemNo;
    }

    public sealed class CoreItemCycleException(string path) : Exception($"BOM circular reference: {path}");

    public static Result Resolve(string itemNo, IReadOnlyList<BomEdge> edges)
    {
        var children = edges
            .Where(e => string.Equals(e.CompItemType, "SUB", StringComparison.OrdinalIgnoreCase))
            .GroupBy(e => e.ParentItemNo, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var cores   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var path    = new List<string> { itemNo };
        bool anySub = false;

        void Walk(string parent, int depth)
        {
            if (depth > MaxDepth || !children.TryGetValue(parent, out var subs)) return;
            foreach (var e in subs)
            {
                anySub = true;
                if (path.Contains(e.CompItemNo, StringComparer.OrdinalIgnoreCase))
                    throw new CoreItemCycleException(string.Join(" > ", path.Append(e.CompItemNo)));
                if (IsCoreName(e.CompItemName)) cores.Add(e.CompItemNo);
                if (!visited.Add(e.CompItemNo)) continue;   // 같은 SUB 가 여러 경로로 — 한 번만 내려간다
                path.Add(e.CompItemNo);
                Walk(e.CompItemNo, depth + 1);
                path.RemoveAt(path.Count - 1);
            }
        }
        Walk(itemNo, 1);

        if (!anySub) return new(Outcome.Self, null);
        return cores.Count switch
        {
            1 => new(Outcome.Core, cores.Single()),
            0 => new(Outcome.Missing, null),
            _ => new(Outcome.Ambiguous, null),
        };
    }

    /// <summary>
    /// 유효 BOM(APPROVED·기간 내, 부모당 EffFrom 최신 버전 1개 — PpRepository.RunMrp 와 같은 선택)에서
    /// 자식이 SUB 인 간선 전부를 읽어 Resolve 에 넘긴다. 표가 작아(수백 행) 품번별로 자르지 않는다.
    /// </summary>
    public static Result Read(SqlConnection conn, SqlTransaction? tx, string itemNo)
    {
        const string sql = """
            WITH eff AS (
                SELECT v.VersionID, v.EffFrom
                FROM   dbo.MD_BomVersion v
                WHERE  v.Status = 'APPROVED'
                  AND (v.EffFrom IS NULL OR v.EffFrom <= @Today)
                  AND (v.EffTo   IS NULL OR v.EffTo   >= @Today)),
            lines AS (
                SELECT b.ParentItemNo, b.CompItemNo,
                       DENSE_RANK() OVER (PARTITION BY b.ParentItemNo ORDER BY e.EffFrom DESC, e.VersionID DESC) AS Rk
                FROM   dbo.MD_Bom b
                JOIN   eff e ON e.VersionID = b.VersionID
                WHERE  ISNULL(b.ActiveFlag,1) = 1 AND b.ParentItemNo IS NOT NULL AND b.CompItemNo IS NOT NULL)
            SELECT l.ParentItemNo, l.CompItemNo, c.ItemType, c.ItemName
            FROM   lines l
            JOIN   dbo.MD_Item c ON c.ItemNo = l.CompItemNo
            WHERE  l.Rk = 1 AND c.ItemType = 'SUB';
            """;
        using var cmd = new SqlCommand(sql, conn, tx);
        cmd.Parameters.Add("@Today", System.Data.SqlDbType.Date).Value = DbClock.Today;
        using var rdr = cmd.ExecuteReader();
        var edges = new List<BomEdge>();
        while (rdr.Read())
            edges.Add(new BomEdge((string)rdr["ParentItemNo"], (string)rdr["CompItemNo"],
                                  rdr["ItemType"] as string, rdr["ItemName"] as string));
        return Resolve(itemNo, edges);
    }
}
