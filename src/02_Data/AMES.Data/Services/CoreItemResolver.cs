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

    public sealed record InjEdge(string ParentItemNo, string CompItemNo, string? CompItemType, string? CompItemName, bool CompInjFlag);

    /// <summary>
    /// 웹 WO 발행(PP-003·PP-004·PP-APS WO 생성)의 코어 판정 — PP-APS BOM 규칙과 같은 사출품 기준(10-14 사용자 결정).
    /// 유효 BOM 을 다단계로 내려가며 <c>MD_Item.InjFlag = 1</c> 인 자식에서 멈추고(사출품), InjFlag 0 인 자식은 서브조립로 보고
    /// 더 내려간다. MATERIAL 은 내려가지 않고, 깊이 상한은 APS 와 같은 <c>ApsRepository.BomWalkMaxDepth</c>, 순환은 APS 처럼 건너뛴다.
    /// 사출품 1개 = Core, 여럿이면 그중 품명이 CORE 로 시작하는 것이 정확히 1개일 때 Core(코어+레일 — 단계 품번 칸이 하나라
    /// 코어를 고른다, 사용자 결정), 아니면 Ambiguous. 사출품이 없는데 MATERIAL 아닌 자식이 있으면 Missing, 그런 자식이 아예 없으면 Self.
    /// POP IMG 코어 스캔 검사(ImgLotRepository)는 아직 <see cref="Read"/>(품명 규칙) — POP 담당이 옮긴다. 테스트: CoreItemResolverTests.
    /// </summary>
    public static Result ResolveByInjFlag(string itemNo, IReadOnlyList<InjEdge> edges)
    {
        var children = edges
            .GroupBy(e => e.ParentItemNo, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var found = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);   // 사출품 → 품명
        var path  = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { itemNo };
        bool anyChild = false;

        void Walk(string parent, int depth)
        {
            if (!children.TryGetValue(parent, out var comps)) return;
            foreach (var e in comps)
            {
                if (path.Contains(e.CompItemNo)) continue;
                bool material = string.Equals(e.CompItemType, "MATERIAL", StringComparison.OrdinalIgnoreCase);
                if (e.CompInjFlag) { anyChild = true; found.TryAdd(e.CompItemNo, e.CompItemName); continue; }
                if (material) continue;
                anyChild = true;
                if (depth >= Repositories.ApsRepository.BomWalkMaxDepth) continue;
                path.Add(e.CompItemNo);
                Walk(e.CompItemNo, depth + 1);
                path.Remove(e.CompItemNo);
            }
        }
        Walk(itemNo, 1);

        if (found.Count == 1) return new(Outcome.Core, found.Keys.Single());
        if (found.Count > 1)
        {
            var named = found.Where(kv => IsCoreName(kv.Value)).Select(kv => kv.Key).ToList();
            return named.Count == 1 ? new(Outcome.Core, named[0]) : new(Outcome.Ambiguous, null);
        }
        return anyChild ? new(Outcome.Missing, null) : new(Outcome.Self, null);
    }

    /// <summary><see cref="ResolveByInjFlag"/> 의 DB 판 — <see cref="Read"/> 와 같은 유효 BOM 선택, 활성 품목의 모든 자식(유형·InjFlag 포함).</summary>
    public static Result ReadByInjFlag(SqlConnection conn, SqlTransaction? tx, string itemNo)
    {
        const string sql = "dbo.SP_POP_BOM_ReadByInjFlag";
        using var cmd = new SqlCommand(sql, conn, tx) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@Today", System.Data.SqlDbType.Date).Value = DbClock.Today;
        using var rdr = cmd.ExecuteReader();
        var edges = new List<InjEdge>();
        while (rdr.Read())
            edges.Add(new InjEdge((string)rdr["ParentItemNo"], (string)rdr["CompItemNo"],
                                  rdr["ItemType"] as string, rdr["ItemName"] as string, Convert.ToBoolean(rdr["InjFlag"])));
        return ResolveByInjFlag(itemNo, edges);
    }

    /// <summary>
    /// 유효 BOM(APPROVED·기간 내, 부모당 EffFrom 최신 버전 1개 — PpRepository.RunMrp 와 같은 선택)에서
    /// 자식이 SUB 인 간선 전부를 읽어 Resolve 에 넘긴다. 표가 작아(수백 행) 품번별로 자르지 않는다.
    /// 품명 규칙 — POP IMG 코어 스캔 검사가 쓴다(웹 WO 발행은 <see cref="ReadByInjFlag"/>).
    /// </summary>
    public static Result Read(SqlConnection conn, SqlTransaction? tx, string itemNo)
    {
        const string sql = "dbo.SP_POP_BOM_Read";
        using var cmd = new SqlCommand(sql, conn, tx) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@Today", System.Data.SqlDbType.Date).Value = DbClock.Today;
        using var rdr = cmd.ExecuteReader();
        var edges = new List<BomEdge>();
        while (rdr.Read())
            edges.Add(new BomEdge((string)rdr["ParentItemNo"], (string)rdr["CompItemNo"],
                                  rdr["ItemType"] as string, rdr["ItemName"] as string));
        return Resolve(itemNo, edges);
    }
}
