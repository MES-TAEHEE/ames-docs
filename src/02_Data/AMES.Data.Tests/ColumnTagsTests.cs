using System.Text.RegularExpressions;
using AMES.Data.Connection;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// 매개변수에 적은 대상 컬럼("테이블.컬럼"·"프로시저.@매개변수")이 개발 DB 에 실제로 있는지(10-13) — 컬럼 이름이 바뀌거나 틀리면
// 저장(ColumnParams.Add)은 예외로 막히고 조회(AddFilter)는 조용히 값 형식으로 돌아가므로 여기서 미리 잡는다
public class ColumnTagsTests
{
    static readonly Regex Direct = new(@"ColumnParams\.(?:Add|AddFilter)\(\s*\w+\s*,\s*""@\w+""\s*,\s*""([^""]+)""", RegexOptions.Compiled);
    static readonly Regex Tuple = new(@"\(\s*""@\w+""\s*,\s*""([A-Za-z_]\w*\.@?\w+)""\s*,", RegexOptions.Compiled);

    static string SrcRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "AMES.sln"))) return d.FullName;
        throw new DirectoryNotFoundException("AMES.sln not found above the test output folder.");
    }

    static List<(string File, string Tag)> Tags()
    {
        var root = SrcRoot(); var list = new List<(string, string)>();
        foreach (var p in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var parts = Path.GetRelativePath(root, p).Split(Path.DirectorySeparatorChar);
            if (parts.Any(x => x is "bin" or "obj" || x.EndsWith("Tests", StringComparison.Ordinal))) continue;
            var text = File.ReadAllText(p);
            foreach (Match m in Direct.Matches(text)) list.Add((Path.GetFileName(p), m.Groups[1].Value));
            foreach (Match m in Tuple.Matches(text)) list.Add((Path.GetFileName(p), m.Groups[1].Value));
        }
        return list;
    }

    [Fact]
    public void Source_has_tags_to_check()
        => Assert.True(Tags().Count > 400, "태그 수집이 비었다 — 정규식이 소스 모양과 어긋남");

    [SkippableFact]
    public void Every_tag_exists_in_the_database()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        using var conn = f.OpenConnection();
        var missing = Tags().Where(t => ColumnParams.TryInfo(conn, t.Tag) is null).Select(t => $"{t.File}: {t.Tag}").Distinct().ToList();
        Assert.True(missing.Count == 0, "DB 에 없는 대상 컬럼: " + string.Join(", ", missing));
    }
}
