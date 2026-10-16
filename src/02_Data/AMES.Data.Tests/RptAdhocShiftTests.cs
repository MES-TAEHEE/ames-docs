using AMES.Data.Repositories;
using AMES.Data.Services;
using Xunit;

namespace AMES.Data.Tests;

/// <summary>RPT-010 생산 실적 교대 차원 — 교대는 공통코드 WORK_SHIFT 창으로 판정한다(읽기 전용, AMES_DEV 필요).</summary>
public class RptAdhocShiftTests
{
    [SkippableFact]
    public void Prod_by_shift_uses_work_shift_codes()
    {
        var f = AmesDevDb.TryFactory();
        Skip.If(f is null, "AMES_DEV 접속 불가");
        var rpt = new RptRepository(f!);

        var rows = rpt.AdhocQuery("PROD", "Shift", new DateTime(2020, 1, 1), new DateTime(2030, 12, 31));
        var total = rpt.AdhocQuery("PROD", "Line", new DateTime(2020, 1, 1), new DateTime(2030, 12, 31));

        using var conn = f!.OpenConnection();
        using var cmd = new Microsoft.Data.SqlClient.SqlCommand(ProdCalendar.ShiftsSql, conn);
        using var rdr = cmd.ExecuteReader();
        var codes = new HashSet<string> { "—" };
        while (rdr.Read()) if (rdr["CodeValue"] is string c) codes.Add(c);

        Assert.All(rows, r => Assert.Contains(r.Key, codes));
        Assert.Equal(total.Sum(r => r.M["Entries"]), rows.Sum(r => r.M["Entries"]));   // 교대로 나눠도 건수는 그대로
    }
}
