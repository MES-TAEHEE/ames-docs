using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Services;

/// <summary>
/// DB 서버 시각 기준의 "지금". 호스트 프로세스의 시계와 DB 의 SYSDATETIME() 차이(Offset)를 한 번 읽어 두고
/// 이후에는 <c>DateTime.Now + Offset</c> 으로 계산한다 — 호출마다 DB 를 가지 않는다.
///
/// 왜: 기록 시각의 정본은 DB(SYSDATETIME, 공장 현지시각)인데 화면 기본값·"오늘" 필터·채번 접두어는 호스트 시계를 썼다.
/// 웹 서버와 DB 서버의 시간대가 다르면(예: 한국시간 PC 의 IIS + 미국 동부시간 DB) 같은 건의 시각이 13시간 어긋난다.
///
/// 동기화는 Web 이 기동 시 1회 + 로그인(회로 시작) 시 <see cref="EnsureSynced"/> 로 한다. Configure 하지 않은 프로세스
/// (POP·API)는 Offset 0 이라 DateTime.Now 와 같다 — 기존 동작 그대로.
/// </summary>
public static class DbClock
{
    private static AmesConnectionFactory? _factory;
    private static long _offsetTicks;
    private static long _syncedAtTick = -1;          // Environment.TickCount64 (호스트 시계 변경에 영향받지 않는 단조 시계)
    private static readonly object Gate = new();

    /// <summary>DB 시각 − 호스트 시각. 동기화 전에는 0.</summary>
    public static TimeSpan Offset => new(Interlocked.Read(ref _offsetTicks));
    // Kind 는 Unspecified — DB 에서 읽은 DATETIME2 값과 같다. Local 로 두면 직렬화·ToUniversalTime 이 호스트 시간대(+09:00 등)를 붙여 틀린 시각이 된다
    public static DateTime Now    => DateTime.SpecifyKind(DateTime.Now + Offset, DateTimeKind.Unspecified);
    public static DateTime Today  => Now.Date;
    public static bool     IsSynced => Interlocked.Read(ref _syncedAtTick) >= 0;
    /// <summary>마지막 동기화 이후 경과. 동기화 전이면 null.</summary>
    public static TimeSpan? SyncAge => IsSynced ? TimeSpan.FromMilliseconds(Environment.TickCount64 - Interlocked.Read(ref _syncedAtTick)) : null;

    public static void Configure(AmesConnectionFactory factory) => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <summary>DB 시각을 읽어 Offset 을 갱신한다. 실패하면 이전 값을 유지하고 false — 시각 보정 때문에 화면이 죽지 않게 예외를 밖으로 내지 않는다.</summary>
    public static bool Sync()
    {
        var f = _factory;
        if (f is null) return false;
        try
        {
            using var conn = f.OpenConnection();
            using var cmd  = new SqlCommand("SELECT SYSDATETIME();", conn);
            var before = DateTime.Now;
            var dbNow  = (DateTime)cmd.ExecuteScalar()!;
            var after  = DateTime.Now;
            // 왕복 시간의 가운데를 호스트 시각으로 본다. 초 단위 이하는 표시·판정에 의미가 없어 100ms 로 정리
            var offset = dbNow - (before + TimeSpan.FromTicks((after - before).Ticks / 2));
            var rounded = TimeSpan.FromMilliseconds(Math.Round(offset.TotalMilliseconds / 100.0) * 100.0);
            Interlocked.Exchange(ref _offsetTicks, rounded.Ticks);
            Interlocked.Exchange(ref _syncedAtTick, Environment.TickCount64);
            return true;
        }
        catch { return false; }
    }

    /// <summary>동기화한 적이 없거나 maxAge 보다 오래됐으면 다시 읽는다. 동시에 여러 회로가 들어와도 한 번만 조회한다.</summary>
    public static void EnsureSynced(TimeSpan maxAge)
    {
        if (SyncAge is { } age && age <= maxAge) return;
        lock (Gate)
        {
            if (SyncAge is { } again && again <= maxAge) return;
            Sync();
        }
    }
}
