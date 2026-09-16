using AMES.Api.Auth;
using AMES.Contracts.Auth;
using AMES.Contracts.Enums;
using AMES.Data.Services;
using AMES.Devices;

namespace AMES.Api.Endpoints;

public static class AuthEndpoints
{
    public sealed record LoginDto(string EmployeeNo, string Pin, string TerminalId, string LineId, string ShiftCode);
    public sealed record BarcodeLoginDto(string Barcode, string TerminalId, string LineId, string ShiftCode);

    public sealed record LoginResultDto(
        string Token, AuthResult Result, string? Reason,
        string? EmployeeNo, string? EmployeeName, string? LineId, string? ShiftCode, DateTime? ExpiresAt);

    public static void MapAuth(this WebApplication app, PopAuthService auth, TokenStore tokens)
    {
        var g = app.MapGroup("/api/auth").WithTags("Auth");

        g.MapPost("/login", (LoginDto body) =>
        {
            var outcome = auth.Login(new LoginRequest
            {
                AttemptedId = body.EmployeeNo,
                Pin         = body.Pin,
                Method      = AuthMethod.Pin,
                TerminalId  = body.TerminalId,
                LineId      = body.LineId,
                ShiftCode   = body.ShiftCode,
            });

            return LoginResult(outcome, tokens);
        });

        g.MapPost("/barcode-login", (BarcodeLoginDto body) =>
        {
            var badge = BadgeScanParser.Parse(body.Barcode);
            if (badge.WorkerNo.Length is 0 or > 20)
                return Results.Ok(new LoginResultDto(
                    Token: "", Result: AuthResult.BadCredentials, Reason: "Invalid employee barcode.",
                    EmployeeNo: null, EmployeeName: null, LineId: null, ShiftCode: null, ExpiresAt: null));

            var outcome = auth.Login(new LoginRequest
            {
                AttemptedId = badge.WorkerNo,
                Pin         = null,
                Method      = AuthMethod.Badge,
                TerminalId  = body.TerminalId,
                LineId      = body.LineId,
                ShiftCode   = body.ShiftCode,
            });

            return LoginResult(outcome, tokens);
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

    private static IResult LoginResult(LoginOutcome outcome, TokenStore tokens)
    {
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
    }
}
