using AMES.Data.Connection;
using Xunit;

namespace AMES.Data.Tests;

// DbHealth 는 프로세스 전역 상태라 연결을 여는 다른 테스트와 겹치면 결과가 바뀐다 — 혼자 돌린다
[CollectionDefinition(nameof(DbHealthTests), DisableParallelization = true)]
public class DbHealthCollection { }

/// <summary>Web 의 DB 장애 배너가 보는 상태: 마지막 연결 결과가 실패면 장애, 성공하면 해제.</summary>
[Collection(nameof(DbHealthTests))]
public class DbHealthTests
{
    [Fact]
    public void Failure_after_success_is_down_and_next_success_clears_it()
    {
        DbHealth.RecordSuccess();
        Thread.Sleep(2);
        DbHealth.RecordFailure();
        Assert.True(DbHealth.IsDown);
        Assert.NotNull(DbHealth.LastFailureUtc);

        Thread.Sleep(2);
        DbHealth.RecordSuccess();
        Assert.False(DbHealth.IsDown);
    }

    [Fact]
    public void Non_sql_exception_is_not_a_db_failure()
    {
        DbHealth.RecordSuccess();
        Assert.False(DbHealth.IsConnectionFailure(new InvalidOperationException("x", new TimeoutException())));
        Assert.False(DbHealth.IsDown);
    }

    // 장애 중 복구 확인 연결은 서버 전체에서 하나·9초마다만 — 화면(세션) 수만큼 동시에 시도하지 않는다(10-09)
    [Fact]
    public void Probe_runs_one_at_a_time_and_waits_for_the_interval()
    {
        DbHealth.ResetProbeForTests();
        DbHealth.RecordSuccess();
        Thread.Sleep(2);
        DbHealth.RecordFailure();
        var t0 = new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc);
        using var gate = new ManualResetEventSlim(false);
        int runs = 0;
        try
        {
            var started = Enumerable.Range(0, 30).AsParallel()
                .Count(_ => DbHealth.TryStartProbe(() => { Interlocked.Increment(ref runs); gate.Wait(5000); }, t0));
            Assert.Equal(1, started);

            gate.Set();
            Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref runs) == 1, 2000));
            Thread.Sleep(200);   // 시험 스레드가 끝나 "진행 중" 표시가 풀릴 때까지
            Assert.False(DbHealth.TryStartProbe(() => { }, t0.AddSeconds(5)));    // 간격 전
            Assert.True(DbHealth.TryStartProbe(() => { }, t0.AddSeconds(10)));    // 간격 뒤
        }
        finally
        {
            gate.Set();
            Thread.Sleep(100);
            DbHealth.RecordSuccess();
            DbHealth.ResetProbeForTests();
        }
    }

    [Fact]
    public void Probe_does_nothing_while_db_is_up()
    {
        DbHealth.ResetProbeForTests();
        DbHealth.RecordSuccess();
        Assert.False(DbHealth.TryStartProbe(() => throw new InvalidOperationException("must not run")));
    }

    // 연결을 열 수 없거나 끊긴 경우만 장애 — 서버 없음(53·40)·시간 초과(-2)·소켓(10054·10060)·DB 열기(4060)·앱 계정 로그인(18456)
    [Theory]
    [InlineData(53, 20)]
    [InlineData(40, 20)]
    [InlineData(-2, 11)]
    [InlineData(10054, 20)]
    [InlineData(10060, 20)]
    [InlineData(4060, 11)]
    [InlineData(18456, 14)]
    [InlineData(0, 20)]     // 번호 없는 전송 계층 오류(심각도 20 이상)
    public void Connection_errors_are_db_failures(int number, byte severity)
        => Assert.True(DbHealth.IsConnectionError(number, severity));

    // 연결은 됐는데 문장이 실패한 경우는 장애가 아니다 — 값 잘림·없는 컬럼·교착·키 중복·외래키·사용자 THROW
    [Theory]
    [InlineData(8152, 16)]
    [InlineData(2628, 16)]
    [InlineData(207, 16)]
    [InlineData(1205, 13)]
    [InlineData(2627, 14)]
    [InlineData(547, 16)]
    [InlineData(50000, 16)]
    public void Statement_errors_are_not_db_failures(int number, byte severity)
        => Assert.False(DbHealth.IsConnectionError(number, severity));
}
