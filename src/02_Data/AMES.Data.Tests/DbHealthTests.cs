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
        Assert.False(DbHealth.IsSqlFailure(new InvalidOperationException("x", new TimeoutException())));
        Assert.False(DbHealth.IsDown);
    }
}
