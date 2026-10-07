using AMES.Data.Repositories;
using Xunit;

namespace AMES.Data.Tests;

/// <summary>SYS-007 감사 로그 조건 검색 — 건수 제한 없음·조건은 DB 에서(읽기 전용, AMES_DEV 필요).</summary>
public class AuditSearchTests
{
    static readonly DateTime From = new(2000, 1, 1), To = new(2100, 1, 1);

    [SkippableFact]
    public void Wide_period_returns_every_row_not_just_200()
    {
        var f = AmesDevDb.TryFactory();
        Skip.If(f is null, "AMES_DEV 접속 불가");
        var rows = new SysRepository(f!).SearchAudit(new SysRepository.AuditFilter(From, To, null, null, null, null));
        Assert.Equal(Convert.ToInt32(AmesDevDb.Scalar(f!, "SELECT COUNT(*) FROM dbo.SYS_AuditLog")), rows.Count);
    }

    [SkippableFact]
    public void Filters_are_applied_in_the_database()
    {
        var f = AmesDevDb.TryFactory();
        Skip.If(f is null, "AMES_DEV 접속 불가");
        var sys = new SysRepository(f!);
        var module = AmesDevDb.Scalar(f!, "SELECT TOP 1 ModuleCode FROM dbo.SYS_AuditLog WHERE ModuleCode IS NOT NULL GROUP BY ModuleCode ORDER BY COUNT(*) DESC") as string;
        Skip.If(module is null, "감사 로그 없음");

        var byModule = sys.SearchAudit(new SysRepository.AuditFilter(From, To, null, module, null, null));
        Assert.NotEmpty(byModule);
        Assert.All(byModule, r => Assert.Equal(module, r.ModuleCode));
        Assert.Equal(Convert.ToInt32(AmesDevDb.Scalar(f!, "SELECT COUNT(*) FROM dbo.SYS_AuditLog WHERE ModuleCode = @M", ("@M", module))), byModule.Count);

        var target = byModule.First(r => !string.IsNullOrEmpty(r.TargetId)).TargetId!;
        var bySearch = sys.SearchAudit(new SysRepository.AuditFilter(From, To, target, null, null, null));
        Assert.Contains(bySearch, r => r.TargetId == target);

        var today = sys.SearchAudit(new SysRepository.AuditFilter(new DateTime(1999, 1, 1), new DateTime(1999, 1, 1), null, null, null, null));
        Assert.All(today, r => Assert.Null(r.EventTs));   // 기간 밖은 시각 없는 행만
    }
}
