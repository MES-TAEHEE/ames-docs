using AMES.Contracts.Dto;
using AMES.Pop.Services;
using Xunit;

namespace AMES.Pop.Tests;

public class AppStateTests
{
    private static PopSessionDto Session(int id = 7) => new()
    {
        SessionId    = id,
        OperatorId   = "op-1",
        EmployeeNo   = "W001",
        EmployeeName = "홍길동",
        TerminalId   = "ST-INJ-01",
        LineId       = "L-INJ-01",
        ShiftCode    = "DAY",
        AuthMethod   = AMES.Contracts.Enums.AuthMethod.Pin,
        StartedAt    = new DateTime(2026, 9, 9, 8, 0, 0),
        ExpiresAt    = new DateTime(2026, 9, 9, 20, 0, 0),
    };

    private static (AppState State, List<(PopSessionDto Session, string Reason)> Closed) Wired()
    {
        var state  = new AppState();
        var closed = new List<(PopSessionDto, string)>();
        state.SigningOut += (s, r) => closed.Add((s, r));
        return (state, closed);
    }

    [Fact]
    public void SignOut_reports_the_session_it_is_closing()
    {
        var (state, closed) = Wired();
        var session = Session();
        state.SignIn(session, "INJ");

        state.SignOut("IDLE");

        Assert.Equal(new[] { (session, "IDLE") }, closed);
        Assert.Null(state.Session);
    }

    [Fact]
    public void SignOut_defaults_the_reason_to_USER()
    {
        var (state, closed) = Wired();
        state.SignIn(Session(), "INJ");

        state.SignOut();

        Assert.Equal("USER", Assert.Single(closed).Reason);
    }

    [Fact]
    public void SignOut_without_a_session_reports_nothing()
    {
        var (state, closed) = Wired();

        state.SignOut();

        Assert.Empty(closed);
    }

    [Fact]
    public void SignOut_closes_a_session_still_waiting_on_pin_setup()
    {
        var (state, closed) = Wired();
        var session = Session();
        state.BeginPinSetup(session);

        state.SignOut("IDLE");

        Assert.Equal(new[] { (session, "IDLE") }, closed);
        Assert.Null(state.PendingSession);
    }

    [Fact]
    public void EndPinSetup_clears_the_pending_session_without_closing_it()
    {
        var (state, closed) = Wired();
        state.BeginPinSetup(Session());

        state.EndPinSetup();

        Assert.Empty(closed);
        Assert.Null(state.PendingSession);
    }

    [Fact]
    public void SignIn_drops_a_pending_pin_setup_without_closing_it()
    {
        var (state, closed) = Wired();
        var session = Session();
        state.BeginPinSetup(session);

        state.SignIn(session, "INJ");

        Assert.Empty(closed);
        Assert.Null(state.PendingSession);
        Assert.Same(session, state.Session);
    }
}
