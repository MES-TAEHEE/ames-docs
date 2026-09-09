using AMES.Data.Connection;
using AMES.Data.Services;
using Xunit;

namespace AMES.InjAgent.Tests;

/// <summary>
/// ProdCalendar.ResolveNow 통합 테스트 — 공통코드 DAY_CUTOFF·WORK_SHIFT 를 실제로 읽는다.
/// DB 미기동 시 skip (InjLotRepositoryTests 와 같은 방식).
/// </summary>
public class ProdCalendarDbTests
{
    static readonly string Conn =
        Environment.GetEnvironmentVariable("AMES_TEST_CONN")
        ?? "Server=192.168.1.100,1433;Database=AMES_DEV;User Id=ames_app;Password=!Dev2026;TrustServerCertificate=True;Encrypt=True;Connect Timeout=10;";

    static AmesConnectionFactory? TryFactory()
    {
        try
        {
            var f = new AmesConnectionFactory(Conn);
            using var c = f.OpenConnection();
            return f;
        }
        catch { return null; }
    }

    [SkippableFact]
    public void ResolveNow_matches_pure_rule_over_seeded_codes()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        using var conn = f.OpenConnection();

        string? cutoffAttr;
        var shifts = new List<(string Code, string? Window)>();
        using (var cmd = new Microsoft.Data.SqlClient.SqlCommand("""
            SELECT TOP 1 Attribute1 FROM dbo.MD_CodeItem
            WHERE GroupCode='DAY_CUTOFF' AND CodeValue='TIME' AND ISNULL(UseFlag,1)=1;
            SELECT CodeValue, Attribute1 FROM dbo.MD_CodeItem
            WHERE GroupCode='WORK_SHIFT' AND ISNULL(UseFlag,1)=1
            ORDER BY ISNULL(SortOrder,0), CodeValue;
            """, conn))
        using (var rdr = cmd.ExecuteReader())
        {
            cutoffAttr = rdr.Read() ? rdr["Attribute1"] as string : null;
            rdr.NextResult();
            while (rdr.Read()) shifts.Add(((string)rdr["CodeValue"], rdr["Attribute1"] as string));
        }
        Skip.If(shifts.Count == 0, "WORK_SHIFT not seeded");

        var (now, prodDate, shift) = ProdCalendar.ResolveNow(conn, null);

        Assert.InRange(now, DateTime.Now.AddMinutes(-5), DateTime.Now.AddMinutes(5));
        var expected = ProdCalendar.Resolve(now, cutoffAttr, shifts);
        Assert.Equal(expected.ProdDate, prodDate);
        Assert.Equal(expected.ShiftCode, shift);
        Assert.Equal(prodDate, prodDate.Date);
    }

    [SkippableFact]
    public void ResolveNow_works_inside_a_transaction()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        using var conn = f.OpenConnection();
        using var tx = conn.BeginTransaction();

        var (now, prodDate, _) = ProdCalendar.ResolveNow(conn, tx);
        Assert.True(prodDate <= now.Date);
        Assert.True(prodDate >= now.Date.AddDays(-1));
        tx.Rollback();
    }
}
