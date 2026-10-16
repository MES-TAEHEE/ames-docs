using AMES.Data.Connection;
using AMES.Data.Repositories;
using AMES.Data.Services;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// 프로그램이 읽는 공통코드 그룹은 MD-030 에서 지울 수 없다(10-12) — 목록이 소스와 어긋나지 않는지도 여기서 지킨다
public class CodeGroupsUsedByCodeTests
{
    [Theory]
    [InlineData("WORK_SHIFT",       true)]
    [InlineData(" work_shift ",     true)]    // 대소문자·공백 무시
    [InlineData("SW_POSYNC_AUTH",   true)]    // Worker 설정은 접두어로
    [InlineData("SW_NEWWORKER",     true)]
    [InlineData("WH_INV_STATUS",    true)]    // DB 프로시저가 읽는 그룹
    [InlineData("LINE_TYPE",        false)]   // 시드에만 있고 아무도 읽지 않는 그룹
    [InlineData("",                 false)]
    [InlineData(null,               false)]
    public void IsUsedByCode(string? group, bool expected)
        => Assert.Equal(expected, CodeGroupsUsedByCode.IsUsedByCode(group));

    [Fact]
    public void Repository_refuses_before_touching_the_database()
    {
        var md = new MasterDataRepository(new AmesConnectionFactory("Server=invalid.invalid;Database=x;Connect Timeout=1;"));
        Assert.Throws<InvalidOperationException>(() => md.DeleteCodeGroup("WORK_SHIFT"));
    }

    static string SrcRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "AMES.sln"))) return d.FullName;
        throw new DirectoryNotFoundException("AMES.sln not found above the test output folder.");
    }

    static string AllSource()
    {
        var root = SrcRoot();
        var files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(p => p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
            .Where(p =>
            {
                var parts = Path.GetRelativePath(root, p).Split(Path.DirectorySeparatorChar);
                return !parts.Any(x => x is "bin" or "obj" || x.EndsWith("Tests", StringComparison.Ordinal))
                    && !p.EndsWith("CodeGroupsUsedByCode.cs", StringComparison.Ordinal);
            });
        return string.Join("\n", files.Select(File.ReadAllText));
    }

    static bool Mentioned(string src, string group) => src.Contains($"\"{group}\"") || src.Contains($"'{group}'");

    // 개발 DB 의 그룹 중 소스에 문자열로 나오는 것은 모두 목록에 있어야 하고, 목록의 그룹은 소스나 DB 프로시저가 실제로 읽어야 한다
    [SkippableFact]
    public void List_matches_the_groups_the_source_reads()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var groups = new List<string>();
        using (var conn = f.OpenConnection())
        using (var cmd = new Microsoft.Data.SqlClient.SqlCommand("SELECT GroupCode FROM dbo.MD_CodeGroup", conn))
        using (var r = cmd.ExecuteReader())
            while (r.Read()) groups.Add(r.GetString(0));
        var src = AllSource();

        var missing = groups.Where(g => !g.StartsWith(CodeGroupsUsedByCode.WorkerPrefix, StringComparison.OrdinalIgnoreCase)
                                        && Mentioned(src, g) && !CodeGroupsUsedByCode.Groups.Contains(g)).ToList();
        Assert.True(missing.Count == 0, "소스가 읽는데 목록에 없는 그룹: " + string.Join(", ", missing));

        var stale = CodeGroupsUsedByCode.Groups.Where(g => !Mentioned(src, g) && !CodeGroupsUsedByCode.ReadByDbProcedure.ContainsKey(g)).ToList();
        Assert.True(stale.Count == 0, "목록에 있지만 아무도 읽지 않는 그룹: " + string.Join(", ", stale));
    }
}
