using AMES.Api.Auth;
using AMES.Contracts.Dto;
using AMES.Contracts.Enums;
using Xunit;

namespace AMES.Api.Tests;

public class TokenStoreTests
{
    [Fact]
    public void Expiration_uses_database_time_not_host_local_or_utc_time()
    {
        var now = new DateTime(2000, 1, 1, 23, 59, 0);
        long tick = 0;
        var store = new TokenStore(() => now, () => tick, 1);
        var session = new PopSessionDto
        {
            SessionId = 1, OperatorId = "TEST", EmployeeNo = "TEST", EmployeeName = "Test",
            TerminalId = "PDA", LineId = "01", ShiftCode = "DAY", AuthMethod = AuthMethod.Badge,
            StartedAt = now, ExpiresAt = now.AddHours(12)
        };
        var token = store.Issue(session);
        Assert.Same(session, store.Resolve(token));
        now = session.ExpiresAt.AddTicks(-1);
        tick = (long)TimeSpan.FromHours(12).TotalSeconds - 1;
        Assert.Same(session, store.Resolve(token));
        now = session.ExpiresAt;
        tick++;
        Assert.Null(store.Resolve(token));
        now = session.StartedAt;
        Assert.Null(store.Resolve(token));
    }

    [Fact]
    public void Database_time_failure_does_not_fall_back_to_host_clock()
    {
        var fail = false;
        long tick = 0;
        var store = new TokenStore(() => fail ? throw new InvalidOperationException("DB unavailable") : new DateTime(2000, 1, 1), () => tick, 1);
        var token = store.Issue(new PopSessionDto
        {
            SessionId = 1, OperatorId = "TEST", EmployeeNo = "TEST", EmployeeName = "Test",
            TerminalId = "PDA", LineId = "01", ShiftCode = "DAY", AuthMethod = AuthMethod.Badge,
            StartedAt = new DateTime(2000, 1, 1), ExpiresAt = new DateTime(2000, 1, 2)
        });
        fail = true;
        tick = 61;
        Assert.Throws<InvalidOperationException>(() => store.Resolve(token));
        Assert.Null(store.Resolve("unknown"));
        fail = false;
        Assert.NotNull(store.Resolve(token));
    }
}
