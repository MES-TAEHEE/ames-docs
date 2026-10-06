using AMES.Data.Aps;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace AMES.Data.Tests.Aps;

/// 원본 응답과의 정규화 비교. 키 정렬, 숫자는 소수 2자리, exclude 경로(`a.b`, 배열은 `[*]`)는 뺀다.
public static class GoldenJson
{
    public static string Normalize(JsonNode? node, string path = "", string[]? exclude = null)
    {
        exclude ??= Array.Empty<string>();
        if (node is JsonObject o)
        {
            var parts = o.OrderBy(k => k.Key, StringComparer.Ordinal)
                .Where(k => !exclude.Contains(path == "" ? k.Key : $"{path}.{k.Key}"))
                .Select(k => $"\"{k.Key}\":{Normalize(k.Value, path == "" ? k.Key : $"{path}.{k.Key}", exclude)}");
            return "{" + string.Join(",", parts) + "}";
        }
        if (node is JsonArray a) return "[" + string.Join(",", a.Select(x => Normalize(x, path + "[*]", exclude))) + "]";
        if (node is JsonValue v && v.TryGetValue<double>(out var d))
            return Math.Round(d, 2).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        return node?.ToJsonString() ?? "null";
    }

    public static void ShouldMatch(object actual, JsonNode expected, params string[] exclude)
    {
        var act = JsonNode.Parse(JsonSerializer.Serialize(actual, ApsJson.Options))!;
        var na = Normalize(act, "", exclude); var ne = Normalize(expected, "", exclude);
        if (na == ne) return;
        var i = 0; while (i < na.Length && i < ne.Length && na[i] == ne[i]) i++;
        var from = Math.Max(0, i - 160);
        var actualSnippet = na.Substring(from, Math.Min(400, na.Length - from));
        var expectedSnippet = ne.Substring(from, Math.Min(400, ne.Length - from));
        Assert.Fail($"first difference near offset {i}:\n  actual  …{actualSnippet}\n  expected…{expectedSnippet}");
    }
}
