using AMES.Api.Auth;
using AMES.Contracts.Dto;
using AMES.Contracts.Enums;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using AMES.Data.Services.Notify;

namespace AMES.Api.Endpoints;

/// <summary>
/// 알림 앱(안드로이드) API — 기기(FCM 토큰) 등록·해제, 내 알림 목록·상세·읽음·안 읽은 수.
/// 로그인은 <c>POST /api/auth/login</c>(사번 + PIN). PIN 로그인 세션만 받는다 — 배지 로그인 세션은 403(PIN 없는 작업자는 앱을 쓸 수 없다, 10-24 사용자 결정).
/// 알림은 사번 기준이고 자기 것만 보인다.
/// </summary>
public static class NotificationEndpoints
{
    public sealed record DeviceReq(string? Token);
    public sealed record QuietHoursReq(string? Start, string? End);
    public sealed record QuietHoursRes(string? Start, string? End, int Devices);

    public static void MapNotifications(this WebApplication app, AmesConnectionFactory factory)
    {
        var repo = new NotificationRepository(factory);
        var g = app.MapGroup("/api/notifications").WithTags("Notifications");

        g.MapPost("/devices", (HttpContext ctx, DeviceReq body) =>
        {
            if (Guard(ctx, out var s) is { } denied) return denied;
            if (!NotifyRules.IsValidToken(body.Token)) return Results.BadRequest(new { message = "A valid FCM token (up to 255 characters, no spaces) is required." });
            repo.RegisterDevice(s.EmployeeNo, body.Token!, Actor(s));
            return Results.NoContent();
        }).WithSummary("Register this device's FCM token for the signed-in employee (moves the token if another employee used it)");

        g.MapPost("/devices/off", (HttpContext ctx, DeviceReq body) =>
        {
            if (Guard(ctx, out var s) is { } denied) return denied;
            if (!NotifyRules.IsValidToken(body.Token)) return Results.BadRequest(new { message = "A valid FCM token is required." });
            return repo.DisableDevice(s.EmployeeNo, body.Token!, Actor(s)) ? Results.NoContent() : Results.NotFound();
        }).WithSummary("Stop PUSH to this device (notification toggle off)");

        g.MapGet("/", (HttpContext ctx, bool? unread, long? before, int? take) =>
        {
            if (Guard(ctx, out var s) is { } denied) return denied;
            return Results.Ok(repo.ListInbox(s.EmployeeNo, unread ?? false, before, take ?? 30));
        }).WithSummary("My notifications, newest first (one row per event; page with before = last id)");

        g.MapGet("/unread-count", (HttpContext ctx) =>
        {
            if (Guard(ctx, out var s) is { } denied) return denied;
            return Results.Ok(new { unread = repo.CountUnread(s.EmployeeNo) });
        }).WithSummary("Number of unread notifications");

        g.MapGet("/{id:long}", (HttpContext ctx, long id) =>
        {
            if (Guard(ctx, out var s) is { } denied) return denied;
            return repo.GetInboxDetail(s.EmployeeNo, id) is { } row ? Results.Ok(row) : Results.NotFound();
        }).WithSummary("Notification detail (historyId from the push data) with the current state of its source (failure, andon, QC hold, PM, spare part)");

        // 방해 금지 시간 — 사람 단위(그 사번의 PUSH 기기 전부). 이 시간에 생긴 알림은 보내지 않고 목록에만 남는다(QUIET).
        g.MapGet("/quiet-hours", (HttpContext ctx) =>
        {
            if (Guard(ctx, out var s) is { } denied) return denied;
            var q = repo.GetQuietHours(s.EmployeeNo);
            return Results.Ok(new QuietHoursRes(NotifyRules.FormatClock(q.Start), NotifyRules.FormatClock(q.End), q.Devices));
        }).WithSummary("My do-not-disturb hours (HH:mm, server local time; null = off) and number of active devices");

        g.MapPut("/quiet-hours", (HttpContext ctx, QuietHoursReq body) =>
        {
            if (Guard(ctx, out var s) is { } denied) return denied;
            if (!NotifyRules.TryParseQuietHours(body.Start, body.End, out var start, out var end, out var error))
                return Results.BadRequest(new { message = error });
            return repo.SetQuietHours(s.EmployeeNo, start, end, Actor(s)) > 0
                ? Results.NoContent()
                : Results.NotFound(new { message = "Register this device first (POST /api/notifications/devices)." });
        }).WithSummary("Set do-not-disturb hours for all my devices — start/end HH:mm (start > end crosses midnight); both empty turns them off");

        g.MapPost("/{id:long}/read", (HttpContext ctx, long id) =>
        {
            if (Guard(ctx, out var s) is { } denied) return denied;
            return repo.MarkRead(s.EmployeeNo, id, Actor(s)) ? Results.NoContent() : Results.NotFound();
        }).WithSummary("Mark a notification as read");
    }

    static IResult? Guard(HttpContext ctx, out PopSessionDto session)
    {
        session = ctx.GetSession()!;
        if (session is null) return Results.Unauthorized();
        if (session.AuthMethod != AuthMethod.Pin)
            return Results.Json(new { message = "Sign in with employee number and PIN to use notifications." }, statusCode: StatusCodes.Status403Forbidden);
        return null;
    }

    static string Actor(PopSessionDto s) => s.EmployeeNo.Length > 20 ? s.EmployeeNo[..20] : s.EmployeeNo;
}
