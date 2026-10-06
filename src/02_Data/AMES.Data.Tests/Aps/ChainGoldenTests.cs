using AMES.Data.Aps;
using AMES.Data.Aps.Contracts;
using AMES.Data.Aps.Domain;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace AMES.Data.Tests.Aps;

/// chain_LQ10.json (231행 × 5일 · pegs · loads · warnings) 재현. 규칙은 tools/rules-lab/chain_lab.py 로 확정.
public class ChainGoldenTests
{
    static readonly Settings S = Fixtures.LoadSettings();
    static readonly Dictionary<string, LineInfo> L = Fixtures.LoadLines();

    /// 원본은 계획이 전부 0 인 행의 자식도 BOM 에서 읽어 edges 에 세지만(526 = 518 + 8) 소요가 0 이라 행으로는 내지 않는다.
    public static readonly (string parent, string child)[] ZeroPlanEdges =
    {
        ("82335-P8010BM1", "D3113-P8010"), ("82335-P8010DNN", "D3113-P8010"), ("82335-P8010JY2", "D3113-P8010"), ("82335-P8010RBQ", "D3113-P8010"),
        ("82345-P8010BM1", "D3123-P8010"), ("82345-P8010DNN", "D3123-P8010"), ("82345-P8010JY2", "D3123-P8010"), ("82345-P8010RBQ", "D3123-P8010"),
    };

    public static ChainRequest RequestFromAutofill(int levels = 4)
    {
        var af = JsonSerializer.Deserialize<PlanBundle>(Fixtures.Json("autofill_LQ10.json")["bundle"]!.ToJsonString(), ApsJson.Options)!;
        return new ChainRequest
        {
            BaseDate = af.BaseDate, Dates = af.Dates, Levels = levels,
            Roots = af.Assembly.Where(r => r.Days.Any(d => d.Supply > 0))
                .Select(r => new ChainRoot { PartNo = r.PartNo, PartName = r.PartName, LineCd = r.LineCd, Plan = r.Days.ToDictionary(d => d.Date, d => d.Supply) }).ToList(),
        };
    }

    public static List<JsonNode> FixtureRows() =>
        Fixtures.Json("chain_LQ10.json")["levels"]!.AsArray().SelectMany(l => l!["rows"]!.AsArray()).Select(r => r!).ToList();

    /// 픽스처 rows[].parents 로 BOM 간선(qtyPer 1)을 복원하고, 계획 0 행의 자식 간선 8개를 더한다.
    public static List<BomEdge> FixtureEdges(List<JsonNode> rows) =>
        rows.SelectMany(r => r["parents"]!.AsArray().Select(p => new BomEdge(p!.GetValue<string>(), r["partNo"]!.GetValue<string>(), 1)))
            .Concat(ZeroPlanEdges.Select(e => new BomEdge(e.parent, e.child, 1))).ToList();

    public static Dictionary<string, PartInfo> FixtureParts(List<JsonNode> rows)
    {
        var d = rows.ToDictionary(r => r["partNo"]!.GetValue<string>(),
            r => new PartInfo(r["partNo"]!.GetValue<string>(), r["lineCd"]?.GetValue<string>(), null, null, 0, r["partName"]?.GetValue<string>(), r["uph"]!.GetValue<double>()), StringComparer.OrdinalIgnoreCase);
        d["D3113-P8010"] = new("D3113-P8010", "M6A100", null, null, 0, "CORE-GARNISH-FR DR UPR, LH");
        d["D3123-P8010"] = new("D3123-P8010", "M6A100", null, null, 0, "CORE-GARNISH-FR DR UPR, RH");
        return d;
    }

    public static Dictionary<string, OpeningInfo> FixtureOpening(List<JsonNode> rows) =>
        rows.ToDictionary(r => r["partNo"]!.GetValue<string>(),
            r => new OpeningInfo(r["openingStock"]!.GetValue<double>(), r["stockCounted"]!.GetValue<bool>(), "2026-09-15", 9), StringComparer.OrdinalIgnoreCase);

    /// pegs 는 total 내림차순이고 동률의 순서는 원본의 BOM 순회 순서(재현 불가)라 (total, partNo) 로 맞춰 비교한다.
    public static string NormalizeChain(JsonNode node)
    {
        var copy = JsonNode.Parse(node.ToJsonString())!;
        foreach (var row in copy["levels"]!.AsArray().SelectMany(l => l!["rows"]!.AsArray()))
        {
            var pegs = row!["pegs"]!.AsArray();
            var sorted = pegs.Select(p => JsonNode.Parse(p!.ToJsonString())!)
                .OrderByDescending(p => p["total"]!.GetValue<double>()).ThenBy(p => p["partNo"]!.GetValue<string>(), StringComparer.Ordinal).ToList();
            pegs.Clear(); foreach (var p in sorted) pegs.Add(p);
        }
        return GoldenJson.Normalize(copy);
    }

    [Fact]
    public void Expand_reproduces_chain_fixture()
    {
        var fx = Fixtures.Json("chain_LQ10.json");
        var rows = FixtureRows();
        var res = ChainExpander.Expand(RequestFromAutofill(), FixtureEdges(rows), FixtureParts(rows), L, FixtureOpening(rows), new ShiftRules(S, L), new StageRules(S, L));
        var got = JsonNode.Parse(JsonSerializer.Serialize(res, ApsJson.Options))!;
        Assert.Equal(NormalizeChain(fx), NormalizeChain(got));
    }
}
