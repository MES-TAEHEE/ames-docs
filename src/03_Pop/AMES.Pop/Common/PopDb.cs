using AMES.Data.Connection;
using AMES.Pop.Services;

namespace AMES.Pop.Common;

/// <summary>
/// 화면용 DB 연결 상태. 상태 자체는 AMES.Data 의 <see cref="DbHealth"/>(연결 성공·실패 기록)가 갖고,
/// 여기서는 연결 확인만 맡는다 — 프로세스 전체에서 검사 주기(설정)마다 한 번, 동시에 하나만 연결해 본다.
/// 상태는 바뀔 때마다 파일에 남겨, 다음 실행이 지난번 끊김 상태로 바로 시작한다(첫 업무 연결의 긴 대기를 피한다, 10-29).
/// </summary>
public static class PopDb
{
    public static bool IsOffline => DbHealth.IsDown;

    // 장애 중 복구 확인. 작업 화면은 5초 조회가 곧 감시라 장애 때만 필요하다.
    public static void ProbeIfOffline() { if (IsOffline) Ping(); }

    // 감시용 시험은 업무 연결과 별개로 짧게 끝낸다 — 업무 연결의 Connect Timeout(10~30초)을 기다리면 끊김 확인이 그만큼 늦다.
    // 업무 연결 풀과 풀을 따로 쓰므로 죽은 풀 연결에 속지도 않는다.
    // 주기·대기는 설정 PopTerminal:Db:ProbeIntervalSeconds·ProbeTimeoutSeconds — 설정을 못 읽는 환경(테스트)은 3초.
    static int ProbeTimeoutSec => Setting(c => c.DbProbeTimeoutSeconds, 3);
    static TimeSpan PingInterval => TimeSpan.FromSeconds(Setting(c => c.DbProbeIntervalSeconds, 3));
    static readonly TimeSpan FreshFor = TimeSpan.FromSeconds(2);
    // 타이머가 조금 일찍 깨어도 한 주기를 건너뛰지 않게
    static readonly TimeSpan PingSlack = TimeSpan.FromMilliseconds(250);

    static int Setting(Func<AppConfig, int> pick, int fallback)
    {
        try { return pick(AppConfig.Current); }
        catch { return fallback; }
    }

    static int  _pinging;
    static long _lastPingTicks, _lastVerifiedTicks;
    // 대기(초)가 설정에서 바뀌면 다음 검사부터 새 값으로 연결한다 — POP 을 다시 켜지 않아도 된다
    static (int Sec, string Conn)? _probe;
    static string ProbeConn
    {
        get
        {
            var sec = ProbeTimeoutSec;
            if (_probe is { } p && p.Sec == sec) return p.Conn;
            var conn = PopServices.ConnectionStringWithTimeout(PopServices.ConnectionString, sec);
            _probe = (sec, conn);
            return conn;
        }
    }

    static (string Conn, AMES.Data.Repositories.MasterDataRepository Repo)? _quick;
    /// <summary>
    /// 감시와 같은 짧은 대기 연결로 읽는 마스터 조회 — DB 가 막 끊겼어도 업무 연결의 긴 대기 없이 몇 초 안에 연결 오류로 끝난다.
    /// 끊겼으면 저장된 사본으로 넘어가야 하는 로그인 화면 조회가 쓴다.
    /// </summary>
    public static AMES.Data.Repositories.MasterDataRepository QuickMaster
    {
        get
        {
            var conn = ProbeConn;
            if (_quick is { } q && q.Conn == conn) return q.Repo;
            var repo = new AMES.Data.Repositories.MasterDataRepository(new AmesConnectionFactory(conn));
            _quick = (conn, repo);
            return repo;
        }
    }

    // ── 주기 검사·상태 저장 ─────────────────────────────────────────────

    internal const string StateFile = "db-health.bin";
    internal sealed class SavedState
    {
        public bool Down { get; set; }
        public DateTime AtUtc { get; set; }
    }

    static System.Threading.Timer? _monitor;
    static TimeSpan _monitorPeriod;
    static bool? _savedDown;

    /// <summary>
    /// 프로그램 시작 때 한 번. 지난번 상태가 끊김이면 바로 오프라인으로 시작해 저장된 정보를 쓰고(DB 가 살아 있으면 첫 검사가 곧 되돌린다),
    /// 화면과 무관하게 검사 주기마다 연결을 확인하며 상태가 바뀌면 파일에 남긴다.
    /// </summary>
    public static void Start()
    {
        if (_monitor is not null) return;
        RestoreSavedState();
        _monitorPeriod = PingInterval;
        _monitor = new System.Threading.Timer(_ => { Ping(); SaveIfChanged(); FollowInterval(); }, null, TimeSpan.Zero, _monitorPeriod);
    }

    // 검사 주기가 설정에서 바뀌면 타이머 주기도 맞춘다
    static void FollowInterval()
    {
        var period = PingInterval;
        if (period == _monitorPeriod) return;
        _monitorPeriod = period;
        _monitor?.Change(period, period);
    }

    /// <summary>파일에 남은 지난 상태를 읽는다 — 끊김이었으면 연결 상태를 끊김으로 둔다.</summary>
    internal static void RestoreSavedState()
    {
        var saved = OfflineStore.Load<SavedState>(StateFile);
        _savedDown = saved?.Down;
        if (saved?.Down == true) DbHealth.RecordFailure();
    }

    /// <summary>지금 상태가 마지막으로 남긴 것과 다르면 파일에 남긴다(실패해도 검사는 계속).</summary>
    internal static void SaveIfChanged()
    {
        var down = IsOffline;
        if (_savedDown == down) return;
        try
        {
            OfflineStore.Save(StateFile, new SavedState { Down = down, AtUtc = DateTime.UtcNow });
            _savedDown = down;
        }
        catch (Exception ex) { PopLog.Append("offline", "PopDb", $"save db state failed: {ex.Message}"); }
    }

    // 화면이 조회하지 않아도 끊김을 알아야 한다 — 정상일 때도 주기적으로 확인한다. 장애 중에는 복구 확인을 겸한다.
    public static void Ping()
    {
        var now = DateTime.UtcNow.Ticks;
        if (now - Interlocked.Read(ref _lastPingTicks) < (PingInterval - PingSlack).Ticks) return;
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
            using var conn = new Microsoft.Data.SqlClient.SqlConnection(ProbeConn);
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
