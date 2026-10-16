using AMES.Data.Services;
using Xunit;

namespace AMES.Data.Tests;

/// <summary>
/// POP·Web 이 "오늘" 을 DB 날짜로 맞추는 근거 — Sync 뒤 DbClock.Now/Today 는 DB 의 SYSDATETIME() 과 같은 날·같은 시각이어야 한다.
/// 호스트 시간대(EDT)와 DB 시간대(복제본은 UTC)가 달라도 성립해야 한다.
/// </summary>
public class DbClockTests
{
    [SkippableFact]
    public void After_sync_now_and_today_follow_the_database_clock()
    {
        var f = AmesDevDb.TryFactory();
        Skip.If(f is null, "AMES_DEV unreachable");

        DbClock.Configure(f!);
        Assert.True(DbClock.Sync(), "Sync must succeed when the DB answers");
        Assert.True(DbClock.IsSynced);

        var dbNow = DbClock.ReadNow(f!);
        var drift = (DbClock.Now - dbNow).Duration();
        Assert.True(drift < TimeSpan.FromSeconds(5), $"DbClock.Now drifted {drift} from SYSDATETIME()");
        Assert.Equal(dbNow.Date, DbClock.Today);
        Assert.Equal(DateTimeKind.Unspecified, DbClock.Now.Kind);
    }
}
