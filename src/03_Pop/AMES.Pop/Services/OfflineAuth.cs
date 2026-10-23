using AMES.Contracts.Auth;
using AMES.Contracts.Dto;
using AMES.Contracts.Enums;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using AMES.Data.Security;
using AMES.Data.Services;
using AMES.Pop.Common;
using Microsoft.Data.SqlClient;

namespace AMES.Pop.Services;

internal sealed record CachedEmployee(
    string EmployeeNo, string EmployeeName, string UserId, string? AccountStatus,
    int FailedLoginCount, string? PinHash, bool IsWorker, bool IsAdmin, string? Department);

/// <summary>오프라인 로그인에 필요한 마스터 사본. 로그아웃 때(그리고 오래됐을 때) 온라인에서 다시 받아 통째로 바꾼다.</summary>
internal sealed class OfflineAuthCache
{
    public DateTime SyncedAtUtc { get; set; }
    public List<CachedEmployee> Employees { get; set; } = new();
    public List<MasterDataRepository.PopLineOptionRow> Lines { get; set; } = new();
    public Dictionary<string, List<MasterDataRepository.StationRow>> Stations { get; set; } = new();
}

/// <summary>오프라인 동안 DB 에 못 쓴 인증 관련 기록 한 건. 복구되면 순서대로 재생한다.</summary>
internal sealed class JournalEntry
{
    public string Kind { get; set; } = "";          // AuthLog | PinFail | LoginOk | SessionOpen | SessionClose
    public DateTime At { get; set; }
    public string? TerminalId { get; set; }
    public string? LineId { get; set; }
    public string? ShiftCode { get; set; }
    public string? AttemptedId { get; set; }
    public string? FailReason { get; set; }
    public string? EmployeeNo { get; set; }
    public string? EmployeeName { get; set; }
    public string? UserId { get; set; }
    public string? Reason { get; set; }
    public bool IsWorker { get; set; }
    public bool HasPin { get; set; }
    public AuthMethod Method { get; set; }
    public AuthResult Result { get; set; }
    public int SessionId { get; set; }              // SessionOpen: 임시(음수) ID / SessionClose: 닫을 ID
}

internal sealed class OfflineJournal
{
    public int LastLocalSessionId { get; set; }
    public List<JournalEntry> Entries { get; set; } = new();
    /// <summary>오프라인에서 틀린 PIN 횟수(사번별) — 복구 때 서버 카운터에 합친다.</summary>
    public Dictionary<string, int> LocalFails { get; set; } = new();
    /// <summary>임시 세션 ID → 복구 때 만든 실제 PR_PopSession ID.</summary>
    public Dictionary<int, int> SessionMap { get; set; } = new();
}

/// <summary>
/// POP 오프라인 로그인(10-23 사용자 결정): 전체 직원 사본을 암호화해 터미널에 두고, DB 가 끊겼을 때 그 사본으로
/// 로그인한다. 배지는 온라인과 같이 PIN 없이 통과하고, 오프라인 동안의 인증 기록·세션·PIN 실패는 저널에 쌓았다가
/// 복구되면 서버로 합쳐 올린다. 오프라인에서 하는 작업(확정·불량 등)은 이 클래스의 범위가 아니다.
/// </summary>
internal static class OfflineAuth
{
    /// <summary>사본이 이보다 오래되면 오프라인 로그인을 받지 않는다 — 퇴사·비활성 반영이 너무 늦어지지 않게.</summary>
    public static TimeSpan MaxAge => _maxAgeOverride ?? ConfiguredMaxAge();
    private static TimeSpan? _maxAgeOverride;
    internal static void SetMaxAgeForTests(TimeSpan? value) => _maxAgeOverride = value;

    // 설정 PopTerminal:Offline:CacheMaxAgeDays — 설정 파일을 못 읽는 환경(테스트)에서는 기본 7일.
    private static TimeSpan ConfiguredMaxAge()
    {
        try { return TimeSpan.FromDays(AppConfig.Current.OfflineCacheMaxAgeDays); }
        catch { return TimeSpan.FromDays(7); }
    }
    private static readonly TimeSpan RefreshAfter = TimeSpan.FromHours(12);
    private static readonly TimeSpan RefreshRetry = TimeSpan.FromMinutes(1);
    private const int MaxFails = 5;   // AuthRepository.IncrementFailedCount 의 잠금 기준과 같다

    private const string CacheFile   = "auth-cache.bin";
    private const string JournalFile = "auth-journal.bin";

    private static readonly object Gate = new();
    private static OfflineAuthCache? _cache;
    private static bool _cacheLoaded;
    private static OfflineJournal? _journal;
    private static int _refreshing, _replaying;
    private static long _lastRefreshTryTicks;

    /// <summary>테스트용 — 메모리 상태를 버리고 파일에서 다시 읽게 한다.</summary>
    internal static void ResetForTests()
    {
        lock (Gate) { _cache = null; _cacheLoaded = false; _journal = null; }
    }

    // ── 사본 ────────────────────────────────────────────────────────────

    public static OfflineAuthCache? Cache
    {
        get
        {
            lock (Gate)
            {
                if (!_cacheLoaded) { _cache = OfflineStore.Load<OfflineAuthCache>(CacheFile); _cacheLoaded = true; }
                return _cache;
            }
        }
    }

    public static bool HasUsableCache
        => Cache is { Employees.Count: > 0 } c && DateTime.UtcNow - c.SyncedAtUtc <= MaxAge;

    private static bool NeedsRefresh
        => Cache is not { } c || c.Employees.Count == 0 || DateTime.UtcNow - c.SyncedAtUtc > RefreshAfter;

    /// <summary>DB 에서 직원·라인·스테이션을 읽어 사본을 통째로 바꾼다. 연결 오류는 호출자에게 던진다.</summary>
    public static void RefreshCache()
    {
        var profiles = PopServices.Auth.ListAllProfiles();
        var admins   = PopServices.Auth.ListAdminUserIds();

        // 웹 계정이 이긴다 — 로그인 화면 픽커·PopAuthService 의 해석 순서와 같다.
        var seen = profiles.Select(p => p.EmployeeNo).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var all  = new List<EmployeeProfileDto>(profiles);
        try { all.AddRange(PopServices.Workers.ListAllActive().Where(w => seen.Add(w.EmployeeNo))); }
        catch (SqlException ex) when (!DbHealth.IsConnectionFailure(ex)) { /* migrate_md_worker.sql 미적용 — 웹 계정만 */ }

        var lines = PopServices.Master.ListPopLineOptions();
        var stations = new Dictionary<string, List<MasterDataRepository.StationRow>>();
        foreach (var l in lines) stations[l.LineID] = PopServices.Master.ListActiveStations(l.LineID);

        var cache = new OfflineAuthCache
        {
            SyncedAtUtc = DateTime.UtcNow,
            Employees = all.Select(p => new CachedEmployee(
                p.EmployeeNo, p.EmployeeName, p.UserId, p.AccountStatus, p.FailedLoginCount,
                p.PinHash, p.IsWorker, !p.IsWorker && admins.Contains(p.UserId), p.Department)).ToList(),
            Lines = lines,
            Stations = stations,
        };
        lock (Gate)
        {
            OfflineStore.Save(CacheFile, cache);
            _cache = cache;
            _cacheLoaded = true;
        }
    }

    /// <summary>로그인 화면이 온라인으로 읽은 라인 목록을 사본에 반영한다(직원 사본이 없으면 아무것도 안 한다).</summary>
    public static void StoreLines(List<MasterDataRepository.PopLineOptionRow> lines)
    {
        lock (Gate)
        {
            if (Cache is not { } c) return;
            c.Lines = lines;
            OfflineStore.Save(CacheFile, c);
        }
    }

    public static void StoreStations(string lineId, List<MasterDataRepository.StationRow> stations)
    {
        lock (Gate)
        {
            if (Cache is not { } c) return;
            c.Stations[lineId] = stations;
            OfflineStore.Save(CacheFile, c);
        }
    }

    /// <summary>화면을 막지 않게 백그라운드로. 동시에 하나만, 실패하면 1분 뒤에야 다시 시도한다.</summary>
    public static void RefreshInBackground(bool force = false)
    {
        if (PopDb.IsOffline) return;
        if (!force && !NeedsRefresh) return;
        var now = DateTime.UtcNow.Ticks;
        if (now - Interlocked.Read(ref _lastRefreshTryTicks) < RefreshRetry.Ticks) return;
        if (Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0) return;
        Interlocked.Exchange(ref _lastRefreshTryTicks, now);
        _ = Task.Run(() =>
        {
            try { RefreshCache(); }
            catch (Exception ex) { PopLog.Append("offline", "OfflineAuth", $"cache refresh failed: {ex.Message}"); }
            finally { Interlocked.Exchange(ref _refreshing, 0); }
        });
    }

    // ── 오프라인 로그인 ─────────────────────────────────────────────────

    public static LoginOutcome Login(LoginRequest req)
    {
        var cache = Cache;
        if (cache is null || cache.Employees.Count == 0)
            return Fail(req, AuthResult.BadCredentials, "offline cache empty");
        if (DateTime.UtcNow - cache.SyncedAtUtc > MaxAge)
            return Fail(req, AuthResult.BadCredentials, "offline cache expired");

        var emp = cache.Employees.FirstOrDefault(e =>
            string.Equals(e.EmployeeNo, req.AttemptedId, StringComparison.OrdinalIgnoreCase));
        if (emp is null) return Fail(req, AuthResult.BadCredentials, "unknown employee");

        if (!IsActive(emp.AccountStatus))
            return Fail(req, AuthResult.InactiveAccount, $"account {emp.AccountStatus ?? "Unknown"}");

        var key = emp.EmployeeNo.ToUpperInvariant();
        if (!emp.IsWorker && emp.FailedLoginCount + LocalFails(key) >= MaxFails)
            return Fail(req, AuthResult.InactiveAccount, "account LOCKED after 5 failures");

        if (req.Method == AuthMethod.Pin
            && (string.IsNullOrEmpty(req.Pin) || !PinHasher.Verify(req.Pin, emp.PinHash)))
        {
            var locked = false;
            if (!emp.IsWorker)
            {
                lock (Gate)
                {
                    var j = Journal;
                    j.LocalFails[key] = j.LocalFails.GetValueOrDefault(key) + 1;
                    j.Entries.Add(new JournalEntry { Kind = "PinFail", At = DbClock.Now, UserId = emp.UserId, EmployeeNo = emp.EmployeeNo });
                    SaveJournal();
                    locked = emp.FailedLoginCount + j.LocalFails[key] >= MaxFails;
                }
            }
            return locked
                ? Fail(req, AuthResult.InactiveAccount, "account LOCKED after 5 failures")
                : Fail(req, AuthResult.BadCredentials, "bad pin");
        }

        var now = DbClock.Now;
        int localId;
        lock (Gate)
        {
            var j = Journal;
            j.LocalFails.Remove(key);
            localId = --j.LastLocalSessionId;
            j.Entries.Add(new JournalEntry
            {
                Kind = "SessionOpen", At = now, SessionId = localId, TerminalId = req.TerminalId, LineId = req.LineId,
                ShiftCode = req.ShiftCode, Method = req.Method, EmployeeNo = emp.EmployeeNo, EmployeeName = emp.EmployeeName,
                UserId = emp.UserId, IsWorker = emp.IsWorker, HasPin = !string.IsNullOrEmpty(emp.PinHash),
            });
            j.Entries.Add(AuthLog(req, AuthResult.Ok, null, now));
            if (!emp.IsWorker)
                j.Entries.Add(new JournalEntry { Kind = "LoginOk", At = now, UserId = emp.UserId, EmployeeNo = emp.EmployeeNo });
            SaveJournal();
        }

        return LoginOutcome.Success(new PopSessionDto
        {
            SessionId    = localId,
            OperatorId   = emp.EmployeeNo,
            EmployeeNo   = emp.EmployeeNo,
            EmployeeName = emp.EmployeeName,
            TerminalId   = req.TerminalId,
            LineId       = req.LineId,
            ShiftCode    = req.ShiftCode,
            AuthMethod   = req.Method,
            StartedAt    = now,
            ExpiresAt    = now + PopSessionRepository.DefaultLifetime,
            IsAdmin      = emp.IsAdmin,
            IsWorker     = emp.IsWorker,
            HasPin       = !string.IsNullOrEmpty(emp.PinHash),
        });
    }

    private static LoginOutcome Fail(LoginRequest req, AuthResult result, string reason)
    {
        Add(AuthLog(req, result, reason, DbClock.Now));
        return LoginOutcome.Failure(result, reason);
    }

    private static JournalEntry AuthLog(LoginRequest req, AuthResult result, string? reason, DateTime at) => new()
    {
        Kind = "AuthLog", At = at, TerminalId = req.TerminalId, AttemptedId = req.AttemptedId,
        Method = req.Method, Result = result, FailReason = reason,
    };

    private static bool IsActive(string? status)
        => string.IsNullOrEmpty(status) || string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase);

    private static int LocalFails(string key) { lock (Gate) return Journal.LocalFails.GetValueOrDefault(key); }

    // ── 저널 ────────────────────────────────────────────────────────────

    // Gate 를 잡고 부른다.
    private static OfflineJournal Journal => _journal ??= OfflineStore.Load<OfflineJournal>(JournalFile) ?? new();

    private static void SaveJournal()
    {
        try { OfflineStore.Save(JournalFile, Journal); }
        catch (Exception ex) { PopLog.Append("offline", "OfflineAuth", $"journal save failed: {ex.Message}"); }
    }

    private static void Add(JournalEntry e) { lock (Gate) { Journal.Entries.Add(e); SaveJournal(); } }

    public static int PendingCount { get { lock (Gate) return Journal.Entries.Count; } }

    /// <summary>오프라인 임시 세션 ID 를 복구 때 만든 실제 PR_PopSession ID 로 — 아직 반영 전이면 null.</summary>
    public static int? MappedSession(int localSessionId)
    {
        lock (Gate) return Journal.SessionMap.TryGetValue(localSessionId, out var real) ? real : null;
    }

    /// <summary>사본 라인 목록에서 그 라인의 공정 코드(INJ/IMG/PNT/…) — 없으면 null.</summary>
    public static string? ProcessOf(string lineId)
        => Cache?.Lines.FirstOrDefault(l => string.Equals(l.LineID, lineId, StringComparison.OrdinalIgnoreCase))?.ProcessCode;

    /// <summary>세션을 닫는 순간 DB 에 못 닿았거나 임시 세션일 때 — 복구되면 닫는다.</summary>
    public static void JournalSessionClose(int sessionId, string reason, string employeeNo)
        => Add(new JournalEntry { Kind = "SessionClose", At = DbClock.Now, SessionId = sessionId, Reason = reason, EmployeeNo = employeeNo });

    // ── 복구 때 재생 ────────────────────────────────────────────────────

    /// <summary>5초마다 불린다 — 연결을 확인하고, 살아 있으면 저널을 서버로 올리고 사본이 오래됐으면 새로 받는다.</summary>
    public static void Tick(AppState state)
    {
        PopDb.Ping();
        if (PopDb.IsOffline) return;
        if (PendingCount > 0) Replay(state);
        RefreshInBackground();
        OfflineWork.Tick(state);   // 인증 기록(임시 세션 → 실제 세션)이 먼저 올라간 뒤에 작업을 올린다
    }

    public static void Replay(AppState state)
    {
        if (PopDb.IsOffline) return;
        if (Interlocked.CompareExchange(ref _replaying, 1, 0) != 0) return;
        try
        {
            while (true)
            {
                JournalEntry? e;
                lock (Gate) e = Journal.Entries.FirstOrDefault();
                if (e is null) break;

                try { Apply(e, state); }
                catch (Exception ex)
                {
                    if (DbHealth.IsConnectionFailure(ex) || PopDb.IsOffline)
                    {
                        PopLog.Append("offline", "OfflineAuth", $"replay paused at {e.Kind}: {ex.Message}");
                        break;   // 남은 기록은 다음 복구에서
                    }
                    // 연결 문제가 아닌 오류는 같은 기록이 영원히 막지 않게 버린다 — 로그만 남는다.
                    PopLog.Append("offline", "OfflineAuth", $"replay dropped {e.Kind} ({e.EmployeeNo ?? e.AttemptedId}): {ex.Message}");
                }

                lock (Gate) { Journal.Entries.Remove(e); SaveJournal(); }
            }
        }
        finally { Interlocked.Exchange(ref _replaying, 0); }
    }

    private static void Apply(JournalEntry e, AppState state)
    {
        switch (e.Kind)
        {
            case "AuthLog":
                PopServices.Sessions.WriteAuthLog(e.TerminalId!, e.AttemptedId!, e.Method, e.Result, Stamp(e));
                break;

            case "PinFail":
                PopServices.Auth.IncrementFailedCount(e.UserId!, e.EmployeeNo);
                break;

            case "LoginOk":
                PopServices.Auth.RecordSuccessfulLogin(e.UserId!, e.EmployeeNo);
                break;

            case "SessionOpen":
            {
                var profile = new EmployeeProfileDto
                {
                    UserId = e.UserId!, UserName = e.EmployeeNo!, EmployeeNo = e.EmployeeNo!,
                    EmployeeName = e.EmployeeName ?? e.EmployeeNo!, PasswordHash = string.Empty,
                    PinHash = e.HasPin ? "-" : null, IsWorker = e.IsWorker,
                };
                var real = PopServices.Sessions.CreateSession(profile, e.TerminalId!, e.LineId!, e.ShiftCode!, e.Method);
                lock (Gate) { Journal.SessionMap[e.SessionId] = real.SessionId; SaveJournal(); }
                // 아직 그 임시 세션으로 일하는 중이면 실제 세션으로 바꿔 끼운다 — 이후 실적은 실제 SessionID 로 남는다.
                state.ReplaceSession(e.SessionId, real);
                break;
            }

            case "SessionClose":
            {
                var id = e.SessionId;
                if (id < 0)
                {
                    int mapped = 0;
                    lock (Gate) Journal.SessionMap.TryGetValue(id, out mapped);
                    if (mapped == 0) break;   // 열린 기록이 없다 — 닫을 것도 없다
                    id = mapped;
                }
                PopServices.Sessions.CloseSession(id, e.Reason ?? "USER", e.EmployeeNo);
                break;
            }
        }
    }

    // PR_PopAuthLog 는 서버 시각으로 찍히므로 오프라인에서 일어난 시각을 사유 칸(40자)에 남긴다.
    private static string Stamp(JournalEntry e)
    {
        var s = $"OFFLINE {e.At:MMdd HHmm}" + (string.IsNullOrEmpty(e.FailReason) ? "" : " " + e.FailReason);
        return s.Length > 40 ? s[..40] : s;
    }
}
