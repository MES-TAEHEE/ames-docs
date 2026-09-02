using AMES.Web.Data;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using MimeKit;

namespace AMES.Web.Services;

/// <summary>
/// MailKit 기반 SMTP 발신자 — appsettings "Smtp" 섹션(Host/Port/User/Password/From/FromName/UseStartTls) 사용.
/// Host 미설정이면 발송 생략(로그만). Program.cs 는 Smtp:Host 가 있을 때만 이 구현을 등록.
/// 비밀번호는 User Secrets/환경변수 주입 권장.
/// </summary>
public sealed class SmtpEmailSender : IEmailSender<ApplicationUser>
{
    readonly IConfiguration _cfg;
    readonly ILogger<SmtpEmailSender> _log;

    public SmtpEmailSender(IConfiguration cfg, ILogger<SmtpEmailSender> log)
    {
        _cfg = cfg;
        _log = log;
    }

    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink) =>
        SendAsync(email, "이메일 인증 / Confirm your email",
            $"A-MES 계정 이메일을 인증해 주세요.<br/><br/><a href='{confirmationLink}'>이메일 인증하기</a>");

    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) =>
        SendAsync(email, "비밀번호 재설정 / Reset your password",
            $"아래 링크에서 비밀번호를 재설정하세요.<br/><br/><a href='{resetLink}'>비밀번호 재설정</a>");

    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) =>
        SendAsync(email, "비밀번호 재설정 코드 / Reset code",
            $"비밀번호 재설정 코드: <b>{resetCode}</b>");

    async Task SendAsync(string to, string subject, string htmlBody)
    {
        var s = _cfg.GetSection("Smtp");
        var host = s["Host"];
        if (string.IsNullOrWhiteSpace(host))
        {
            _log.LogWarning("SMTP Host 미설정 — 메일 발송 생략: {To} / {Subject}", to, subject);
            return;
        }

        var port     = int.TryParse(s["Port"], out var p) ? p : 587;
        var userName = s["User"];
        var password = s["Password"];
        var from     = s["From"] ?? userName ?? "no-reply@localhost";
        var fromName = s["FromName"] ?? "A-MES";
        var startTls = !bool.TryParse(s["UseStartTls"], out var t) || t;

        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(fromName, from));
        msg.To.Add(MailboxAddress.Parse(to));
        msg.Subject = subject;
        msg.Body    = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        // 465=암시적 SSL, 587=STARTTLS, 그 외/미지정=Auto
        var secure = port == 465 ? SecureSocketOptions.SslOnConnect
                   : startTls    ? SecureSocketOptions.StartTls
                   :               SecureSocketOptions.Auto;

        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(host, port, secure);
            if (!string.IsNullOrWhiteSpace(userName))
                await client.AuthenticateAsync(userName, password ?? "");
            await client.SendAsync(msg);
            await client.DisconnectAsync(true);
            _log.LogInformation("메일 발송: {To} / {Subject}", to, subject);
        }
        catch (Exception ex)
        {
            // 발송 실패해도 가입/재설정 흐름은 중단하지 않음(운영에서 재발송/관리자 조치)
            _log.LogError(ex, "SMTP 발송 실패: {To} / {Subject}", to, subject);
        }
    }
}
