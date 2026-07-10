using AMES.Api.Auth;
using AMES.Contracts.Auth;
using AMES.Contracts.Enums;
using AMES.Data.Services;

namespace AMES.Api.Endpoints;

public static class AuthEndpoints
{
    public sealed record LoginDto(string EmployeeNo, string Pin, string TerminalId, string LineId, string ShiftCode);

    public sealed record LoginResultDto(
        string Token, AuthResult Result, string? Reason,
        string? EmployeeNo, string? EmployeeName, string? LineId, string? ShiftCode, DateTime? ExpiresAt);

    public static void MapAuth(this WebApplication app, PopAuthService auth, TokenStore tokens)
    {
        var g = app.MapGroup("/api/auth").WithTags("Auth");

        g.MapPost("/login", (LoginDto body) =>
        {
            LoginOutcome outcome;
            try
            {
                outcome = auth.Login(new LoginRequest
                {
                    AttemptedId = body.EmployeeNo,
                    Pin         = body.Pin,
                    Method      = AuthMethod.Pin,
                    TerminalId  = body.TerminalId,
                    LineId      = body.LineId,
                    ShiftCode   = body.ShiftCode,
                });
            }
            catch (Exception ex)
            {
                app.Logger.LogError(ex, "PDA login failed for {EmployeeNo}", body.EmployeeNo);
                return Results.Ok(new LoginResultDto(
                    Token: "", Result: AuthResult.InactiveAccount,
                    Reason: "Authentication service unavailable",
                    EmployeeNo: null, EmployeeName: null, LineId: null, ShiftCode: null, ExpiresAt: null));
            }

            if (!outcome.IsSuccess || outcome.Session is null)
                return Results.Ok(new LoginResultDto(
                    Token: "", Result: outcome.Result, Reason: outcome.FailReason,
                    EmployeeNo: null, EmployeeName: null, LineId: null, ShiftCode: null, ExpiresAt: null));

            var token = tokens.Issue(outcome.Session);
            return Results.Ok(new LoginResultDto(
                Token: token, Result: outcome.Result, Reason: null,
                EmployeeNo: outcome.Session.EmployeeNo,
                EmployeeName: outcome.Session.EmployeeName,
                LineId: outcome.Session.LineId,
                ShiftCode: outcome.Session.ShiftCode,
                ExpiresAt: outcome.Session.ExpiresAt));
        });

        g.MapPost("/logout", (HttpContext ctx) =>
        {
            var token = ctx.Request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase).Trim();
            tokens.Revoke(token);
            return Results.NoContent();
        });

        g.MapGet("/me", (HttpContext ctx) =>
        {
            var s = ctx.GetSession();
            return s is null ? Results.Unauthorized() : Results.Ok(s);
        });
    }
}
