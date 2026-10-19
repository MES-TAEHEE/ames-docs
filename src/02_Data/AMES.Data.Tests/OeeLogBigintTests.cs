using AMES.Data.Repositories;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// 10-19 — MNT_OEELog.OEELogID 가 bigint(migrate_mnt_oeelog_bigint.sql)여도, 적용 전 int 여도 MNT-003·RPT-006 조회가 읽는지
public class OeeLogBigintTests
{
    [SkippableFact]
    public void Oee_rows_read_the_log_id_whatever_its_integer_type()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var mnt = new MntRepository(f!);
        var (min, max) = mnt.OeeDateExtent();
        Skip.If(min is null || max is null, "no MNT_OEELog rows");

        var rows = mnt.ListOeeRange(min!.Value, max!.Value);
        var count = Convert.ToInt32(Scalar(f!, "SELECT COUNT(*) FROM dbo.MNT_OEELog WHERE AggDate IS NOT NULL"));
        Assert.Equal(count, rows.Count);
        Assert.All(rows, r => Assert.True(r.OeeLogId > 0));
        Assert.Equal(rows.Count, rows.Select(r => r.OeeLogId).Distinct().Count());
    }
}
