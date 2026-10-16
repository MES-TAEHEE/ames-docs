using AMES.Data.Aps;
using AMES.Data.Aps.Contracts;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace AMES.Data.Tests.Aps;

/// 픽스처를 DTO 로 읽고 다시 써도 같은 JSON 이어야 한다 — DTO 에 빠진 필드가 있으면 여기서 드러난다.
public class DtoRoundtripTests
{
    [Theory]
    [InlineData("plan_LQ10.json")]
    [InlineData("plan_CV01.json")]
    [InlineData("plan_H1A100.json")]
    public void PlanResponse_roundtrips_fixture(string name)
    {
        var text = File.ReadAllText(Fixtures.Path(name));
        var dto = JsonSerializer.Deserialize<PlanResponse>(text, ApsJson.Options)!;
        GoldenJson.ShouldMatch(dto, JsonNode.Parse(text)!);
    }

    [Fact]
    public void ChainResponse_roundtrips_fixture()
    {
        var text = File.ReadAllText(Fixtures.Path("chain_LQ10.json"));
        var dto = JsonSerializer.Deserialize<ChainResponse>(text, ApsJson.Options)!;
        GoldenJson.ShouldMatch(dto, JsonNode.Parse(text)!);
    }

    [Fact]
    public void Autofill_and_calc_roundtrip_fixture()
    {
        var a = File.ReadAllText(Fixtures.Path("autofill_LQ10.json"));
        GoldenJson.ShouldMatch(JsonSerializer.Deserialize<AutofillResponse>(a, ApsJson.Options)!, JsonNode.Parse(a)!);
        var c = File.ReadAllText(Fixtures.Path("calc_LQ10.json"));
        GoldenJson.ShouldMatch(JsonSerializer.Deserialize<CalcResponse>(c, ApsJson.Options)!, JsonNode.Parse(c)!);
    }

    [Fact]
    public void StagesSuggest_roundtrips_fixture()
    {
        var text = File.ReadAllText(Fixtures.Path("stages_suggest.json"));
        GoldenJson.ShouldMatch(JsonSerializer.Deserialize<StageSuggestResponse>(text, ApsJson.Options)!, JsonNode.Parse(text)!);
    }
}
