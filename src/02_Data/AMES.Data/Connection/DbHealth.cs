namespace AMES.Data.Connection;

/// <summary>
/// 프로세스 안의 DB 연결 상태 — <see cref="AmesConnectionFactory.OpenConnection"/> 의 성공·실패를 기록한다.
/// 화면이 DB 오류를 잡아 빈 목록으로 보여 줘도 "DB 장애"를 따로 안내할 수 있게 하려는 것이다(마지막 결과가 실패면 장애).
/// </summary>
public static class DbHealth
{
    static long _lastOkTicks, _lastFailTicks;

    public static void RecordSuccess() => Interlocked.Exchange(ref _lastOkTicks, DateTime.UtcNow.Ticks);
    public static void RecordFailure() => Interlocked.Exchange(ref _lastFailTicks, DateTime.UtcNow.Ticks);

    public static bool IsDown => Interlocked.Read(ref _lastFailTicks) > Interlocked.Read(ref _lastOkTicks);

    /// <summary>
    /// 예외(내부 예외 포함)가 SQL Server 오류인지 — EF Core(Identity)처럼 이 팩터리를 거치지 않는 연결의 실패도
    /// 여기서 판정해 장애로 기록한다.
    /// </summary>
    public static bool IsSqlFailure(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
            if (e is Microsoft.Data.SqlClient.SqlException) { RecordFailure(); return true; }
        return false;
    }

    /// <summary>장애가 이어지는 동안 첫 실패가 아니라 마지막 실패 시각(UTC).</summary>
    public static DateTime? LastFailureUtc
    {
        get { var t = Interlocked.Read(ref _lastFailTicks); return t > 0 ? new DateTime(t, DateTimeKind.Utc) : null; }
    }
}
