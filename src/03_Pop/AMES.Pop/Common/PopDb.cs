using AMES.Data.Connection;

namespace AMES.Pop.Common;

/// <summary>
/// 화면용 DB 연결 상태. 상태 자체는 AMES.Data 의 <see cref="DbHealth"/>(연결 성공·실패 기록)가 갖고,
/// 여기서는 연결 확인만 맡는다 — 시계 틱마다 불러도 프로세스 전체에서 9초에 한 번, 동시에 하나만 연결해 본다.
/// </summary>
public static class PopDb
{
    public static bool IsOffline => DbHealth.IsDown;

    // 장애 중 복구 확인. 작업 화면은 5초 조회가 곧 감시라 장애 때만 필요하다.
    public static void ProbeIfOffline() { if (IsOffline) Ping(); }

    // 감시용 시험은 업무 연결과 별개로 짧게 끝낸다 — 업무 연결의 Connect Timeout(10~30초)을 기다리면 끊김 확인이 그만큼 늦다.
    // 업무 연결 풀과 풀을 따로 쓰므로 죽은 풀 연결에 속지도 않는다.
    const int ProbeTimeoutSec = 3;
    static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(3);
    static readonly TimeSpan FreshFor     = TimeSpan.FromSeconds(2);

    static int  _pinging;
    static long _lastPingTicks, _lastVerifiedTicks;
    static string? _probeConn;

    // 로그인 화면은 조회가 없어 DB 가 끊겨도 아무도 모른다 — 정상일 때도 주기적으로 확인한다. 장애 중에는 복구 확인을 겸한다.
    public static void Ping()
    {
        var now = DateTime.UtcNow.Ticks;
        if (now - Interlocked.Read(ref _lastPingTicks) < PingInterval.Ticks) return;
        if (Interlocked.CompareExchange(ref _pinging, 1, 0) != 0) return;
        Interlocked.Exchange(ref _lastPingTicks, now);
        _ = Task.Run(() =>
        {
            try { Verify(); } catch { }
            finally { Interlocked.Exchange(ref _pinging, 0); }
        });
    }

    /// <summary>
    /// 지금 DB 가 응답하는지 짧은 시간 안에 확인하고 그 결과를 연결 상태(<see cref="DbHealth"/>)에 반영한다.
    /// 방금(2초 안에) 성공했으면 다시 연결하지 않는다. 로그인처럼 곧 DB 를 쓸 일 앞에서 부른다 — 끊겼으면 업무 연결의 긴 대기 없이 바로 오프라인 경로로 간다.
    /// </summary>
    public static bool Verify()
    {
        if (DateTime.UtcNow.Ticks - Interlocked.Read(ref _lastVerifiedTicks) < FreshFor.Ticks && !IsOffline) return true;
        try
        {
            _probeConn ??= PopServices.ConnectionStringWithTimeout(
                PopServices.ConnectionString, ProbeTimeoutSec);
            using var conn = new Microsoft.Data.SqlClient.SqlConnection(_probeConn);
            conn.Open();
            using var cmd = new Microsoft.Data.SqlClient.SqlCommand("SELECT 1", conn) { CommandTimeout = ProbeTimeoutSec };
            cmd.ExecuteScalar();
            DbHealth.RecordSuccess();
            Interlocked.Exchange(ref _lastVerifiedTicks, DateTime.UtcNow.Ticks);
            return true;
        }
        catch (Exception ex) when (DbHealth.IsConnectionFailure(ex)) { return false; }   // 판정이 실패도 기록한다
        catch { return true; }   // 연결은 됐고 문장만 실패 — 장애가 아니다
    }
}
