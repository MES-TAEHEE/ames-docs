using AMES.Contracts.Auth;
using AMES.Contracts.Dto;
using AMES.Contracts.Enums;
using AMES.Data.Security;
using AMES.Pop.Services;
using Xunit;

namespace AMES.Pop.Tests;

// OfflineAuth 는 프로세스 전역 상태(사본·저널 파일)를 쓴다 — 같은 컬렉션에서 차례로 돌린다.
[Collection("OfflineAuth")]
public sealed class OfflineAuthTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ames-offline-" + Guid.NewGuid().ToString("N"));
    private readonly string _origDir = OfflineStore.Dir;

    public OfflineAuthTests()
    {
        OfflineStore.Dir = _dir;
        OfflineAuth.ResetForTests();
    }

    public void Dispose()
    {
        OfflineStore.Dir = _origDir;
        OfflineAuth.ResetForTests();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static CachedEmployee Emp(string no, string? pin = "1234", string? status = "Active",
                                      bool worker = false, int fails = 0, bool admin = false)
        => new(no, "Name " + no, "uid-" + no, status, fails, pin is null ? null : PinHasher.Hash(pin), worker, admin, null);

    private static void Seed(DateTime syncedUtc, params CachedEmployee[] emps)
        => OfflineStore.Save("auth-cache.bin", new OfflineAuthCache { SyncedAtUtc = syncedUtc, Employees = emps.ToList() });

    private static LoginRequest Req(string id, string? pin, AuthMethod method = AuthMethod.Pin) => new()
    {
        AttemptedId = id, Pin = pin, Method = method, TerminalId = "ST-1", LineId = "LINE-1", ShiftCode = "A",
    };

    [Fact]
    public void Cache_file_is_encrypted_not_plain_json()
    {
        Seed(DateTime.UtcNow, Emp("E001"));
        var bytes = File.ReadAllBytes(OfflineStore.PathOf("auth-cache.bin"));
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain("E001", text);
        Assert.DoesNotContain("EmployeeNo", text);
    }

    [Fact]
    public void Correct_pin_logs_in_with_a_temporary_negative_session_and_queues_the_records()
    {
        Seed(DateTime.UtcNow, Emp("E001", admin: true));
        var outcome = OfflineAuth.Login(Req("E001", "1234"));

        Assert.True(outcome.IsSuccess);
        Assert.True(outcome.Session!.SessionId < 0);
        Assert.Equal("E001", outcome.Session.OperatorId);
        Assert.True(outcome.Session.IsAdmin);
        // SessionOpen + AuthLog + LoginOk
        Assert.Equal(3, OfflineAuth.PendingCount);
    }

    [Fact]
    public void Each_offline_login_gets_its_own_session_id()
    {
        Seed(DateTime.UtcNow, Emp("E001"), Emp("E002"));
        var a = OfflineAuth.Login(Req("E001", "1234")).Session!.SessionId;
        var b = OfflineAuth.Login(Req("E002", "1234")).Session!.SessionId;
        Assert.NotEqual(a, b);
        Assert.True(a < 0 && b < 0);
    }

    [Fact]
    public void Badge_login_needs_no_pin_for_a_cached_employee()
    {
        Seed(DateTime.UtcNow, Emp("W001", pin: null, worker: true));
        var outcome = OfflineAuth.Login(Req("W001", null, AuthMethod.Badge));
        Assert.True(outcome.IsSuccess);
        Assert.True(outcome.Session!.IsWorker);
        Assert.False(outcome.Session.HasPin);
    }

    [Fact]
    public void Unknown_employee_is_refused()
    {
        Seed(DateTime.UtcNow, Emp("E001"));
        var outcome = OfflineAuth.Login(Req("E999", null, AuthMethod.Badge));
        Assert.False(outcome.IsSuccess);
        Assert.Equal(AuthResult.BadCredentials, outcome.Result);
    }

    [Fact]
    public void Inactive_account_is_refused_even_with_the_right_pin()
    {
        Seed(DateTime.UtcNow, Emp("E001", status: "Locked"));
        var outcome = OfflineAuth.Login(Req("E001", "1234"));
        Assert.Equal(AuthResult.InactiveAccount, outcome.Result);
    }

    [Fact]
    public void Wrong_pin_five_times_locks_and_the_right_pin_no_longer_works()
    {
        Seed(DateTime.UtcNow, Emp("E001"));
        for (var i = 0; i < 4; i++)
            Assert.Equal(AuthResult.BadCredentials, OfflineAuth.Login(Req("E001", "0000")).Result);

        Assert.Equal(AuthResult.InactiveAccount, OfflineAuth.Login(Req("E001", "0000")).Result);
        Assert.Equal(AuthResult.InactiveAccount, OfflineAuth.Login(Req("E001", "1234")).Result);
    }

    [Fact]
    public void Server_side_failures_count_toward_the_offline_lock()
    {
        Seed(DateTime.UtcNow, Emp("E001", fails: 4));
        Assert.Equal(AuthResult.InactiveAccount, OfflineAuth.Login(Req("E001", "0000")).Result);
    }

    [Fact]
    public void Workers_are_never_locked_by_wrong_pins()
    {
        Seed(DateTime.UtcNow, Emp("W001", worker: true));
        for (var i = 0; i < 8; i++) OfflineAuth.Login(Req("W001", "0000"));
        Assert.True(OfflineAuth.Login(Req("W001", "1234")).IsSuccess);
    }

    [Fact]
    public void A_good_login_clears_the_local_failure_count()
    {
        Seed(DateTime.UtcNow, Emp("E001"));
        for (var i = 0; i < 3; i++) OfflineAuth.Login(Req("E001", "0000"));
        Assert.True(OfflineAuth.Login(Req("E001", "1234")).IsSuccess);
        for (var i = 0; i < 4; i++) Assert.Equal(AuthResult.BadCredentials, OfflineAuth.Login(Req("E001", "0000")).Result);
    }

    [Fact]
    public void Cache_older_than_the_limit_is_refused()
    {
        Seed(DateTime.UtcNow - OfflineAuth.MaxAge - TimeSpan.FromHours(1), Emp("E001"));
        Assert.False(OfflineAuth.Login(Req("E001", "1234")).IsSuccess);
        Assert.False(OfflineAuth.HasUsableCache);
    }

    [Fact]
    public void Missing_cache_is_refused()
    {
        var outcome = OfflineAuth.Login(Req("E001", "1234"));
        Assert.False(outcome.IsSuccess);
        Assert.False(OfflineAuth.HasUsableCache);
    }

    [Fact]
    public void Journal_survives_a_restart()
    {
        Seed(DateTime.UtcNow, Emp("E001"));
        OfflineAuth.Login(Req("E001", "1234"));
        var before = OfflineAuth.PendingCount;

        OfflineAuth.ResetForTests();   // 프로세스 재시작처럼 메모리만 버린다

        Assert.Equal(before, OfflineAuth.PendingCount);
    }

    [Fact]
    public void ReplaceSession_swaps_only_the_matching_temporary_session()
    {
        var state = new AppState();
        var temp = Session(-3);
        state.SignIn(temp, "INJ");
        var raised = 0;
        state.SessionReplaced += () => raised++;

        state.ReplaceSession(-9, Session(100));          // 다른 ID — 무시
        Assert.Same(temp, state.Session);
        Assert.Equal(0, raised);

        var real = Session(100);
        state.ReplaceSession(-3, real);
        Assert.Same(real, state.Session);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void ReplaceSession_does_not_look_like_a_new_login()
    {
        var state = new AppState();
        state.SignIn(Session(-1), "INJ");
        var changes = 0;
        state.OnChange += () => changes++;
        state.ReplaceSession(-1, Session(7));
        Assert.Equal(0, changes);   // 라벨 발행기가 재로그인으로 오해해 초기화하면 안 된다
    }

    private static PopSessionDto Session(int id) => new()
    {
        SessionId = id, OperatorId = "E001", EmployeeNo = "E001", EmployeeName = "n", TerminalId = "ST-1",
        LineId = "LINE-1", ShiftCode = "A", AuthMethod = AuthMethod.Pin,
        StartedAt = DateTime.Now, ExpiresAt = DateTime.Now.AddHours(12),
    };
}
