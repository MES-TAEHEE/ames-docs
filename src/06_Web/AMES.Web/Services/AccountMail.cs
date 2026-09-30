using AMES.Web.Components.Account;
using AMES.Web.Data;
using Microsoft.AspNetCore.Identity;

namespace AMES.Web.Services;

/// <summary>
/// 계정 메일(이메일 인증·비밀번호 재설정)을 실제로 보낼 수 있는지와, 못 보낼 때의 대체 동작을 정한다.
/// SMTP(Smtp:Host)가 설정돼 있으면 메일 링크만 쓰고, 없으면 — 비밀번호 재설정은 관리자(SYS-001) 안내,
/// 이메일 인증은 개발 환경에서만 링크를 화면에 보이고 운영에서는 관리자가 SYS-001 에서 인증 처리한다.
/// </summary>
public sealed class AccountMail(IEmailSender<ApplicationUser> sender, IWebHostEnvironment env)
{
    /// <summary>Smtp:Host 가 설정돼 실제 메일이 나간다(Program.cs 가 그때만 SmtpEmailSender 를 등록).</summary>
    public bool CanSend => sender is not IdentityNoOpEmailSender;

    /// <summary>메일을 못 보내는 개발 환경에서만 인증 링크를 화면에 보인다 — 운영에서 보이면 메일 확인 없이 인증된다.</summary>
    public bool ShowLinkOnScreen => !CanSend && env.IsDevelopment();

    /// <summary>메일을 못 보내는 운영 환경 — 이메일 인증을 관리자가 SYS-001 에서 대신 처리한다.</summary>
    public bool AdminConfirmsEmail => !CanSend;
}
