using AMES.Api.Auth;
using AMES.Contracts.Auth;
using AMES.Contracts.Dto;
using AMES.Contracts.Enums;
using AMES.Data.Services;
using AMES.Data.Repositories;
using AMES.Devices;

namespace AMES.Api.Endpoints;

public static class AuthEndpoints
{
    public sealed record LoginDto(string EmployeeNo, string Pin, string TerminalId, string LineId, string ShiftCode);
    public sealed record BarcodeLoginDto(string Barcode, string TerminalId, string LineId, string ShiftCode);

    public sealed record LoginResultDto(
        string Token, AuthResult Result, string? Reason,
        string? EmployeeNo, string? EmployeeName, string? LineId, string? ShiftCode, DateTime? ExpiresAt);

    public static void MapAuth(this WebApplication app, PopAuthService auth, TokenStore tokens, PopSessionRepository sessions)
    {
        var g = app.MapGroup("/api/auth").WithTags("Auth");

        g.MapPost("/login", (LoginDto body) =>
        {
            if (!LoginInput.Valid(body.EmployeeNo, 20) || !LoginInput.ValidContext(body.TerminalId, body.LineId, body.ShiftCode))
                return Results.BadRequest(new { message = "Employee, terminal and line must be within 20 bytes; shift within 10 bytes." });
            if (!app.Environment.IsDevelopment() && PdaScenarioUsers.IsTestAccount(body.EmployeeNo))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            var outcome = auth.Login(new LoginRequest
            {
                AttemptedId = body.EmployeeNo,
                Pin         = body.Pin,
                Method      = AuthMethod.Pin,
                TerminalId  = body.TerminalId,
                LineId      = body.LineId,
                ShiftCode   = body.ShiftCode,
            });

            return LoginResult(outcome, tokens, sessions);
        }).RequireRateLimiting("login");

        g.MapPost("/barcode-login", (BarcodeLoginDto body) =>
        {
            var badge = BadgeScanParser.Parse(body.Barcode);
            if (!badge.IsEosFormat || !LoginInput.Valid(badge.WorkerNo, 20)
                || !LoginInput.ValidContext(body.TerminalId, body.LineId, body.ShiftCode))
                return Results.BadRequest(new { message = "Scan a valid EOS employee badge and select a terminal, line and shift." });
            if (!app.Environment.IsDevelopment() && PdaScenarioUsers.IsTestAccount(badge.WorkerNo))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            var outcome = auth.Login(new LoginRequest
            {
                AttemptedId = badge.WorkerNo,
                Pin         = null,
                Method      = AuthMethod.Badge,
                TerminalId  = body.TerminalId,
                LineId      = body.LineId,
                ShiftCode   = body.ShiftCode,
            });

            return LoginResult(outcome, tokens, sessions);
        }).RequireRateLimiting("login");

        g.MapPost("/logout", (HttpContext ctx) =>
        {
            if (ctx.GetSession() is not { } session) return Results.Unauthorized();
            var token = ctx.Request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase).Trim();
            sessions.CloseSession(session.SessionId, "LOGOUT", session.EmployeeNo);
            tokens.Revoke(token);
            return Results.NoContent();
        });

        g.MapGet("/me", (HttpContext ctx) =>
        {
            var s = ctx.GetSession();
            return s is null ? Results.Unauthorized() : Results.Ok(s);
        });
    }

    private static IResult LoginResult(LoginOutcome outcome, TokenStore tokens, PopSessionRepository sessions)
    {
        if (!outcome.IsSuccess || outcome.Session is null)
            return Results.Ok(new LoginResultDto(
                Token: "", Result: outcome.Result, Reason: outcome.FailReason,
                EmployeeNo: null, EmployeeName: null, LineId: null, ShiftCode: null, ExpiresAt: null));

        string token;
        try { token = tokens.Issue(outcome.Session); }
        catch
        {
            try { sessions.CloseSession(outcome.Session.SessionId, "TOKEN_FAILED", outcome.Session.EmployeeNo); }
            catch { /* Keep the token-persistence failure as the original error. */ }
            throw;
        }
        return Results.Ok(new LoginResultDto(
            Token: token, Result: outcome.Result, Reason: null,
            EmployeeNo: outcome.Session.EmployeeNo,
            EmployeeName: outcome.Session.EmployeeName,
            LineId: outcome.Session.LineId,
            ShiftCode: outcome.Session.ShiftCode,
            ExpiresAt: outcome.Session.ExpiresAt));
    }
}
