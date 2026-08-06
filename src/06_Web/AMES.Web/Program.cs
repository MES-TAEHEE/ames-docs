using AMES.Data.Connection;
using AMES.Data.Repositories;
using AMES.Web.Components;
using AMES.Web.Components.Account;
using AMES.Web.Data;
using AMES.Web.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Radzen;

// EUC-KR(cp51949) — SRM 주간 구매계획(.xls=HTML) 디코딩용
System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveServerComponents(options =>
    {
        options.DetailedErrors = true;
    });

builder.Services.AddRadzenComponents();
builder.Services.AddRadzenCookieThemeService(options =>
{
    options.Name = "ames-theme";
    options.Duration = TimeSpan.FromDays(365);
});

builder.Services.AddLocalization();

builder.Services.AddScoped<AMES.Web.Services.PageHeaderState>();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityUserAccessor>();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

// ── Identity tables live in AMES_DEV alongside the operational data ────
var connectionString = builder.Configuration.GetConnectionString("AMES")
    ?? throw new InvalidOperationException("Connection string 'AMES' missing in appsettings.json.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        // 자기가입은 이메일 자기인증 필수. (관리자 생성 계정은 EmailConfirmed=true 로 생성해 영향 없음)
        options.SignIn.RequireConfirmedAccount   = true;
        options.Password.RequireDigit            = true;
        // 최소 길이는 SYS_Config(PASSWORD_MIN_LEN) 기준 ConfigPasswordValidator 가 동적 관장 → 내장 게이트는 완화
        options.Password.RequiredLength          = 1;
        options.Password.RequireNonAlphanumeric  = false;
        options.Password.RequireUppercase        = false;
        options.Password.RequireLowercase        = false;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

// 비밀번호 최소 길이 = SYS_Config(PASSWORD_MIN_LEN) 동적 검증 (앱 재시작 없이 Config 저장 시 반영)
builder.Services.AddSingleton<AMES.Web.Services.AppSecurityState>();
builder.Services.AddScoped<IPasswordValidator<ApplicationUser>, AMES.Web.Services.ConfigPasswordValidator>();
// 이메일 발신: Smtp:Host 설정이 있으면 실제 SMTP 발송, 없으면 NoOp(개발환경은 Register 화면에 인증링크 노출)
if (!string.IsNullOrWhiteSpace(builder.Configuration["Smtp:Host"]))
    builder.Services.AddSingleton<IEmailSender<ApplicationUser>, AMES.Web.Services.SmtpEmailSender>();
else
    builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<PermissionService>();
builder.Services.AddHttpClient();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo
    {
        Title       = "AMES Web API",
        Version     = "v1",
        Description = "AMES 사무실 포탈 유틸리티 엔드포인트. 인증은 쿠키 기반(ASP.NET Identity)입니다."
    });
});

// ── Shared data layer (reused from POP) ─────────────────────────────────
var factory = new AmesConnectionFactory(connectionString);
builder.Services.AddSingleton(factory);

// 세션(인증 쿠키) 타임아웃 = SYS_Config.SESSION_TIMEOUT_MIN(분), 슬라이딩 만료.
// AppSessionState 캐시에서 읽어 옵션 구성 → Config 저장 시 옵션캐시 무효화로 앱 재시작 없이 반영.
// 자동 새로고침 화면은 /keep-alive 핑으로 세션 유지.
builder.Services.AddSingleton<AMES.Web.Services.AppSessionState>();
builder.Services.AddSingleton<Microsoft.Extensions.Options.IConfigureNamedOptions<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>>(sp =>
    new Microsoft.Extensions.Options.ConfigureNamedOptions<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>(
        IdentityConstants.ApplicationScheme,
        o =>
        {
            o.ExpireTimeSpan    = TimeSpan.FromMinutes(sp.GetRequiredService<AMES.Web.Services.AppSessionState>().Minutes);
            o.SlidingExpiration = true;
        }));
builder.Services.AddSingleton(sp => new WorkOrderRepository(factory));
builder.Services.AddSingleton(sp => new EquipmentRepository(factory));
builder.Services.AddSingleton(sp => new MasterDataRepository(factory));
builder.Services.AddSingleton(sp => new ProductionRepository(factory));
builder.Services.AddSingleton(sp => new DefectRepository(factory));
builder.Services.AddSingleton(sp => new PntRepository(factory));
builder.Services.AddSingleton(sp => new QcRepository(factory));
builder.Services.AddSingleton(sp => new PpRepository(factory));
builder.Services.AddSingleton(sp => new MntRepository(factory));
builder.Services.AddSingleton(sp => new RptRepository(factory));
builder.Services.AddSingleton(sp => new WarehouseRepository(factory));
builder.Services.AddSingleton(sp => new SysRepository(factory));
builder.Services.AddSingleton(sp => new AuthRepository(factory));
builder.Services.AddSingleton(sp => new LineScheduleRepository(factory));
builder.Services.AddSingleton(sp => new OeeRepository(factory));
builder.Services.AddSingleton<ServerMonitorService>();
// LANGUAGE_DEFAULT(SYS_Config) 캐시 — 컬처 강제/언어 스위처 판정
builder.Services.AddSingleton<AMES.Web.Services.AppLanguageState>();

var app = builder.Build();

// ── First-run admin seed ───────────────────────────────────────────────
// Ensures `admin@ames.local / Dev2026!` exists so a fresh checkout can
// sign in without a manual register step. No-op on subsequent boots.
using (var scope = app.Services.CreateScope())
{
    var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var roleMgr = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    const string email = "admin@ames.local";
    const string adminRole = "Admin";
    if (await roleMgr.FindByNameAsync(adminRole) is null)
    {
        var roleRes = await roleMgr.CreateAsync(new IdentityRole(adminRole));
        if (!roleRes.Succeeded)
            app.Logger.LogWarning("admin role seed failed: {Errs}", string.Join("; ", roleRes.Errors.Select(e => e.Description)));
    }

    var adminUser = await userMgr.FindByEmailAsync(email);
    if (adminUser is null)
    {
        var u = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
        var res = await userMgr.CreateAsync(u, "Dev2026!");
        if (!res.Succeeded)
            app.Logger.LogWarning("admin seed failed: {Errs}", string.Join("; ", res.Errors.Select(e => e.Description)));
        else
        {
            adminUser = u;
            app.Logger.LogInformation("seeded admin@ames.local / Dev2026!");
        }
    }

    if (adminUser is not null && !await userMgr.IsInRoleAsync(adminUser, adminRole))
    {
        var roleRes = await userMgr.AddToRoleAsync(adminUser, adminRole);
        if (!roleRes.Succeeded)
            app.Logger.LogWarning("admin role assignment failed: {Errs}", string.Join("; ", roleRes.Errors.Select(e => e.Description)));
    }
}

// ── Role seed: SYS_RolePermission.RoleName → AspNetRoles + RoleID backfill ──
// Idempotent: skips roles that already exist, skips rows where RoleID is set.
using (var scope = app.Services.CreateScope())
{
    var roleMgr     = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var connFactory = scope.ServiceProvider.GetRequiredService<AmesConnectionFactory>();

    // 1) Collect distinct RoleNames from SYS_RolePermission
    var roleNames = new List<string>();
    using (var conn = connFactory.OpenConnection())
    using (var cmd  = new SqlCommand(
        "SELECT DISTINCT RoleName FROM dbo.SYS_RolePermission WHERE RoleName IS NOT NULL ORDER BY RoleName", conn))
    using (var rdr  = cmd.ExecuteReader())
        while (rdr.Read()) roleNames.Add((string)rdr["RoleName"]);

    // 2) Create missing roles via RoleManager (handles normalization & ConcurrencyStamp)
    foreach (var name in roleNames)
    {
        if (await roleMgr.FindByNameAsync(name) is not null) continue;
        var res = await roleMgr.CreateAsync(new IdentityRole(name));
        if (res.Succeeded)
            app.Logger.LogInformation("Role seeded: {Name}", name);
        else
            app.Logger.LogWarning("Role seed failed '{Name}': {Errs}", name,
                string.Join("; ", res.Errors.Select(e => e.Description)));
    }

    // 3) Back-fill SYS_RolePermission.RoleID where still NULL
    using (var conn = connFactory.OpenConnection())
    using (var cmd  = new SqlCommand("""
        UPDATE rp
        SET    rp.RoleID = ar.Id
        FROM   dbo.SYS_RolePermission rp
        JOIN   dbo.AspNetRoles         ar ON ar.Name = rp.RoleName
        WHERE  rp.RoleID IS NULL
        """, conn))
    {
        int updated = cmd.ExecuteNonQuery();
        if (updated > 0)
            app.Logger.LogInformation("SYS_RolePermission.RoleID backfilled: {N} rows", updated);
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "AMES Web API v1");
    c.RoutePrefix = "swagger";
});

// HTTPS redirect/HSTS only when an HTTPS port is actually configured.
// IIS sets ANCM_HTTPS_PORT automatically when an HTTPS binding exists.
// This prevents "Failed to determine the https port" on HTTP-only IIS.
var httpsPort = app.Configuration.GetValue<int?>("ASPNETCORE_HTTPS_PORT")
             ?? app.Configuration.GetValue<int?>("ANCM_HTTPS_PORT");
if (httpsPort.HasValue)
{
    app.UseHsts();
    app.UseHttpsRedirection();
}


// 서식(날짜/숫자)은 특정 문화권(ko-KR/en-US), 리소스(resx)는 중립(ko/en)
var locOptions = new RequestLocalizationOptions
{
    DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture(culture: "ko-KR", uiCulture: "ko")
};
locOptions.AddSupportedCultures("ko-KR", "en-US", "es-MX", "es-ES");
locOptions.AddSupportedUICultures("ko", "en", "es");
// LANGUAGE_DEFAULT 비활성 시 en-US 강제(쿠키보다 우선) — 맨 앞에 삽입
locOptions.RequestCultureProviders.Insert(0, new AMES.Web.Services.LanguageDefaultCultureProvider());
app.UseRequestLocalization(locOptions);

app.UseStaticFiles();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", at = DateTime.UtcNow }))
    .WithTags("System")
    .WithSummary("헬스 체크")
    .WithDescription("서버 상태를 반환합니다.");

// 자동 새로고침 화면의 세션 유지용 핑 — 인증 요청이 슬라이딩 만료를 갱신
app.MapGet("/keep-alive", () => Results.NoContent())
    .RequireAuthorization()
    .ExcludeFromDescription();

app.MapGet("/culture/set", (string culture, string redirectUri, HttpContext ctx) =>
{
    ctx.Response.Cookies.Append(
        Microsoft.AspNetCore.Localization.CookieRequestCultureProvider.DefaultCookieName,
        Microsoft.AspNetCore.Localization.CookieRequestCultureProvider.MakeCookieValue(
            new Microsoft.AspNetCore.Localization.RequestCulture(culture)),
        new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true });
    // Nav.Uri returns absolute URL — extract local path only for LocalRedirect
    var localPath = Uri.TryCreate(redirectUri, UriKind.Absolute, out var u) ? u.PathAndQuery : "/";
    return Results.LocalRedirect(localPath);
}).ExcludeFromDescription();

app.Run();
