using AMES.Api.Auth;
using AMES.Api.Endpoints;
using AMES.Contracts.Dto;
using AMES.Contracts.Enums;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace AMES.Api.Tests;

public class SysSecurityTests
{
    [Fact]
    public void Sys_admin_gate_rejects_anonymous_and_non_admin_sessions()
    {
        var ctx = new DefaultHttpContext();
        Assert.Equal(401, (SysEndpoints.RequireAdmin(ctx) as IStatusCodeHttpResult)?.StatusCode);

        ctx.Items[BearerAuth.SessionKey] = Session(isAdmin: false);
        Assert.Equal(403, (SysEndpoints.RequireAdmin(ctx) as IStatusCodeHttpResult)?.StatusCode);

        ctx.Items[BearerAuth.SessionKey] = Session(isAdmin: true);
        Assert.Null(SysEndpoints.RequireAdmin(ctx));
    }

    private static PopSessionDto Session(bool isAdmin) => new()
    {
        SessionId = 1,
        OperatorId = "TEST",
        EmployeeNo = "TEST",
        EmployeeName = "Test",
        TerminalId = "T",
        LineId = "L",
        ShiftCode = "DAY",
        AuthMethod = AuthMethod.Pin,
        StartedAt = DateTime.UtcNow,
        ExpiresAt = DateTime.UtcNow.AddHours(1),
        IsAdmin = isAdmin
    };
}
