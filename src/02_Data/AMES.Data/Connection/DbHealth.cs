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
    /// 예외(내부 예외 포함)가 DB 연결 장애인지 — EF Core(Identity)처럼 이 팩터리를 거치지 않는 연결의 실패도
    /// 여기서 판정해 장애로 기록한다. 연결은 됐는데 문장이 실패한 경우(값 잘림·없는 컬럼·교착·키 중복 등)는
    /// 장애가 아니다 — 그것까지 장애로 기록하면 전 사용자에게 장애 배너가 뜨고 로그인 화면이 엉뚱한 원인을 안내한다.
    /// </summary>
    public static bool IsConnectionFailure(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
            if (e is Microsoft.Data.SqlClient.SqlException sql && IsConnectionError(sql.Number, sql.Class))
            {
                RecordFailure();
                return true;
            }
        return false;
    }

    // 연결을 열 수 없거나 끊긴 경우의 오류 번호(SqlClient 네트워크·로그인 오류). 심각도 20 이상은 연결을 끊는 치명 오류다.
    static readonly HashSet<int> ConnectionErrorNumbers =
    [
        -2,      // 시간 초과(연결·로그인 응답 없음)
        -1, 2, 53, 40, 64, 121, 233, 258,           // 서버를 찾을 수 없음·연결 실패·네트워크 경로
        10053, 10054, 10060, 10061, 11001,          // 소켓 끊김·거부·시간 초과·호스트 없음
        4060,    // DB 를 열 수 없음
        18456,   // 앱 계정(ames_app) 로그인 실패 — 사용자 계정이 아니라 접속 설정 문제다
    ];

    /// <summary>SQL 오류 번호·심각도가 연결 장애인지(순수 판정, 테스트용으로 공개).</summary>
    public static bool IsConnectionError(int number, byte severity)
        => severity >= 20 || ConnectionErrorNumbers.Contains(number);

    /// <summary>장애가 이어지는 동안 첫 실패가 아니라 마지막 실패 시각(UTC).</summary>
    public static DateTime? LastFailureUtc
    {
        get { var t = Interlocked.Read(ref _lastFailTicks); return t > 0 ? new DateTime(t, DateTimeKind.Utc) : null; }
    }

    // ── 복구 확인(연결 시험) ─────────────────────────────────────────────
    public static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(9);
    static int  _probing;
    static long _lastProbeTicks;

    /// <summary>
    /// 장애 중 DB 가 살아났는지 연결해 본다 — 프로세스 전체에서 동시에 하나, <see cref="ProbeInterval"/> 마다 한 번만.
    /// 화면(세션)마다 시도하면 접속자 수만큼 연결이 몰리고 각 시도가 Connect Timeout 동안 스레드 풀을 잡는다(10-09).
    /// 시험은 스레드 풀에서 돌고, 성공·실패 기록은 <paramref name="probe"/> 가 여는 연결(AmesConnectionFactory)이 남긴다.
    /// 반환: 이번 호출이 시험을 시작했으면 true.
    /// </summary>
    public static bool TryStartProbe(Action probe) => TryStartProbe(probe, DateTime.UtcNow);

    internal static bool TryStartProbe(Action probe, DateTime nowUtc)
    {
        if (!IsDown) return false;
        if (nowUtc.Ticks - Interlocked.Read(ref _lastProbeTicks) < ProbeInterval.Ticks) return false;
        if (Interlocked.CompareExchange(ref _probing, 1, 0) != 0) return false;
        Interlocked.Exchange(ref _lastProbeTicks, nowUtc.Ticks);
        _ = Task.Run(() =>
        {
            try { probe(); } catch { }
            finally { Interlocked.Exchange(ref _probing, 0); }
        });
        return true;
    }

    internal static void ResetProbeForTests()
    {
        Interlocked.Exchange(ref _probing, 0);
        Interlocked.Exchange(ref _lastProbeTicks, 0);
    }
}
