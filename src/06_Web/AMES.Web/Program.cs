using AMES.Data.Connection;
using AMES.Data.Repositories;
using AMES.Web.Components;
using AMES.Web.Components.Account;
using AMES.Web.Data;
using AMES.Web.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
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
    })
    // JS → .NET 로 돌아오는 값(차트 PNG base64 등)이 기본 32KB 를 넘을 수 있어 넉넉히 둔다
    .AddHubOptions(o => o.MaximumReceiveMessageSize = 4 * 1024 * 1024);

builder.Services.AddRadzenComponents();
builder.Services.AddRadzenCookieThemeService(options =>
{
    options.Name = "ames-theme";
    options.Duration = TimeSpan.FromDays(365);
});

// 앱풀 loadUserProfile·setProfileEnvironment 가 꺼져 있으면 Data Protection 이 ephemeral 키로 떨어져
// 재활용마다 전원 로그아웃된다. IIS 설정에 기대지 않도록 키를 고정 폴더에 둔다.
// 배포 폴더 안은 안 된다 — publish-web.ps1 의 robocopy /MIR 가 게시 때마다 지운다.
var dpKeyDir = builder.Configuration["DataProtection:KeyPath"];
if (string.IsNullOrWhiteSpace(dpKeyDir))
    dpKeyDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                            "AMES", "DataProtection-Keys", "AMES.Web");
string? dpKeyError = null;
try
{
    Directory.CreateDirectory(dpKeyDir);
    var probe = Path.Combine(dpKeyDir, $".probe-{Guid.NewGuid():N}");
    File.WriteAllText(probe, "");
    File.Delete(probe);

    var dp = builder.Services.AddDataProtection()
        .SetApplicationName("AMES.Web")
        .PersistKeysToFileSystem(new DirectoryInfo(dpKeyDir));
    // 머신 범위 DPAPI 는 사용자 프로필 없이 동작한다
    if (OperatingSystem.IsWindows()) dp.ProtectKeysWithDpapi(protectToLocalMachine: true);
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
{
    // 쓰기 권한이 없으면 기동을 막지 않고 프레임워크 기본 동작(프로필 → 레지스트리 → ephemeral)으로 둔다
    dpKeyError = ex.Message;
}

builder.Services.AddLocalization();

builder.Services.AddScoped<AMES.Web.Services.PageHeaderState>();
builder.Services.AddSingleton<ScreenCatalogNotifier>();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityUserAccessor>();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

// 인증 스킴 2개: 내부 Identity 쿠키 + 외부 개방 화면(/portal) 전용 쿠키(AmesPortal).
// 기본 스킴은 요청에 외부 쿠키가 있으면 AmesPortal, 없으면 Identity 로 고르는 선택형이다.
// 외부 쿠키는 경로 "/" 로 둔다 — 경로를 /portal 로 좁히면 Blazor 회로(/_blazor)에 쿠키가 안 실려 로그인 직후 권한 없음이 된다.
var authBuilder = builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = AMES.Web.Services.PortalAuth.DynamicScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    });
authBuilder.AddIdentityCookies();
authBuilder
    .AddCookie(AMES.Web.Services.PortalAuth.Scheme, o =>
    {
        o.Cookie.Name        = AMES.Web.Services.PortalAuth.CookieName;
        o.Cookie.HttpOnly    = true;
        o.Cookie.SameSite    = SameSiteMode.Lax;
        o.LoginPath          = AMES.Web.Services.PortalAuth.LoginPath;
        o.AccessDeniedPath   = "/unauthorized";
        o.SlidingExpiration  = true;
    })
    .AddPolicyScheme(AMES.Web.Services.PortalAuth.DynamicScheme, "AMES cookie selector", o =>
    {
        // 외부 쿠키가 있으면 AmesPortal. 없으면 Identity — 단, 쿠키가 하나도 없는 /portal 요청은 AmesPortal 로 보내
        // 미인증 챌린지가 내부 로그인이 아니라 /portal/login 으로 가게 한다(내부 쿠키가 있으면 내부 사용자 그대로).
        o.ForwardDefaultSelector = ctx =>
        {
            if (ctx.Request.Cookies.ContainsKey(AMES.Web.Services.PortalAuth.CookieName)) return AMES.Web.Services.PortalAuth.Scheme;
            if (AMES.Web.Services.PortalAuth.IsPortalPath(ctx.Request.Path)
                && !ctx.Request.Cookies.ContainsKey(".AspNetCore." + IdentityConstants.ApplicationScheme)) return AMES.Web.Services.PortalAuth.Scheme;   // 내부 쿠키 기본 이름
            return IdentityConstants.ApplicationScheme;
        };
    });

// 인가 정책: 기본 정책([Authorize] 만 붙은 내부 화면 66개·AuthorizeView)은 내부 Identity 스킴으로 인증된 사용자만 통과 →
// 외부 쿠키로 내부 화면 URL 을 직접 쳐도 전부 거부된다. 외부 화면은 PortalAccess(내부 사용자 또는 외부 역할).
builder.Services.AddAuthorization(options =>
{
    options.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireAssertion(ctx => AMES.Web.Services.PortalAuth.IsInternalUser(ctx.User))
        .Build();
    options.AddPolicy(AMES.Web.Services.PortalAuth.Policy, p => p
        .RequireAuthenticatedUser()
        .RequireAssertion(ctx => AMES.Web.Services.PortalAuth.AllowsPortalAccess(ctx.User)));
});

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
    .AddClaimsPrincipalFactory<AMES.Web.Services.AmesClaimsPrincipalFactory>()   // 로그인 클레임에 행위자 코드(사번) — CreatedBy/ModifiedBy varchar(20)
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
builder.Services.AddScoped<AuditLogger>();   // MD·SYS 등록/수정/삭제 → SYS_AuditLog
builder.Services.AddScoped<AMES.Web.Services.MenuCatalog>();   // 좌측 메뉴·홈 사이트맵 공용 화면 카탈로그(SYS_Screen WEB)
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
// 외부 포탈 쿠키도 같은 SESSION_TIMEOUT_MIN 을 따른다
builder.Services.AddSingleton<Microsoft.Extensions.Options.IConfigureNamedOptions<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>>(sp =>
    new Microsoft.Extensions.Options.ConfigureNamedOptions<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>(
        AMES.Web.Services.PortalAuth.Scheme,
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
builder.Services.AddSingleton(sp => new FinishedGoodsRepository(factory));
builder.Services.AddSingleton(sp => new SysRepository(factory));
builder.Services.AddSingleton(sp => new AuthRepository(factory));
builder.Services.AddSingleton(sp => new WorkerRepository(factory));
builder.Services.AddSingleton(sp => new LineSupervisorRepository(factory));
builder.Services.AddSingleton(sp => new LineScheduleRepository(factory));
builder.Services.AddSingleton(sp => new OeeRepository(factory));
builder.Services.AddSingleton<ServerMonitorService>();
builder.Services.AddSingleton<AMES.Web.Services.PoSyncClient>();   // PP-002 API 가져오기 → AMES.Api PO Sync Worker 수동 실행
// LANGUAGE_DEFAULT(SYS_Config) 캐시 — 컬처 강제/언어 스위처 판정
builder.Services.AddSingleton<AMES.Web.Services.AppLanguageState>();

var app = builder.Build();

if (dpKeyError is null) app.Logger.LogInformation("Data Protection keys -> {Dir}", dpKeyDir);
else app.Logger.LogWarning("Data Protection key folder {Dir} is not writable ({Error}) — falling back to framework default; logins may drop on app pool recycle", dpKeyDir, dpKeyError);

// DB 서버 시각 기준의 Now/Today (AMES.Data.Services.DbClock) — 기동 시 1회 맞추고, 이후 로그인(회로 시작) 때 TopBar 가
// 10분보다 오래됐을 때만 다시 읽는다. DB 가 아직 안 뜬 상태여도 실패를 삼키고 Offset 0(호스트 시계)으로 계속 간다.
DbClock.Configure(factory);
DbClock.Sync();

// ── Startup seeds ──────────────────────────────────────────────────────
// 여기서 예외가 새어나가면 ANCM 이 프로세스를 못 올려 모든 요청이 500 이 되고,
// 화면에는 아무 단서도 남지 않는다. DB 가 늦게 뜨거나 잠시 끊겨도 앱은 기동돼야 한다.
// 시드는 모두 idempotent 하므로 실패해도 다음 재기동에서 다시 시도된다.
async Task RunSeedAsync(string name, Func<IServiceScope, Task> seed)
{
    try
    {
        using var scope = app.Services.CreateScope();
        await seed(scope);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "{Seed} seed skipped — database unavailable at startup", name);
    }
}

// ── First-run admin seed ───────────────────────────────────────────────
// Ensures `admin@ames.local / Dev2026!` exists so a fresh checkout can
// sign in without a manual register step. No-op on subsequent boots.
await RunSeedAsync("admin", async scope =>
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
});

// ── Role seed: SYS_RolePermission.RoleName → AspNetRoles + RoleID backfill ──
// Idempotent: skips roles that already exist, skips rows where RoleID is set.
await RunSeedAsync("role", async scope =>
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
});

// Configure the HTTP request pipeline.
// 외부 개방 화면(/portal)에서 난 예외는 내부 셸(MainLayout)의 /Error 가 아니라 맨몸 /portal/error 로 — 외부 사용자에게 내부 메뉴가 보이지 않게
app.UseWhen(ctx => AMES.Web.Services.PortalAuth.IsPortalPath(ctx.Request.Path),
    branch => branch.UseExceptionHandler("/portal/error", createScopeForErrors: true));

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

// 위조 방지 토큰이 만료·불일치한 폼 POST(재배포·앱풀 재활용 전에 열어 둔 로그인/로그아웃 화면에서 제출, 또는
// Data Protection 키가 바뀐 경우)는 본문 없는 HTTP 400 으로 끝나 사용자에게 "서버 400 오류"로만 보인다.
// /Account 의 폼 POST 에 한해 같은 화면의 GET 으로 돌려보내 새 토큰을 받게 한다. API(JSON)·Blazor 허브는 건드리지 않는다.
// 주의: 검증이 실패한 요청에서 Request.HasFormContentType·Request.Form 을 읽으면 프레임워크가 InvalidOperationException 을 던진다
// (500 으로 악화) — Content-Type 헤더 문자열만 본다.
app.Use(async (ctx, next) =>
{
    await next();
    var contentType = ctx.Request.ContentType ?? "";
    bool isFormPost = contentType.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase)
                   || contentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase);
    if (ctx.Response.StatusCode == StatusCodes.Status400BadRequest && !ctx.Response.HasStarted
        && HttpMethods.IsPost(ctx.Request.Method) && isFormPost
        && (ctx.Request.Path.StartsWithSegments("/Account") || AMES.Web.Services.PortalAuth.IsPortalPath(ctx.Request.Path)))
    {
        var target = ctx.Request.Path.StartsWithSegments("/Account/Logout") ? "/Account/Login" : ctx.Request.Path.Value!;
        ctx.Response.Redirect(target + "?expired=1");
    }
});

// 외부 노출 게이트: appsettings Portal:ExternalHosts 에 적힌 호스트명(예 portal.example.com)으로 들어온 요청은
// 외부 화면(/portal)·Blazor 회로·정적 파일만 허용하고 나머지는 403. 목록이 비어 있으면(기본) 아무것도 막지 않는다.
// IIS URL Rewrite 모듈 없이 소스로 관리하려는 것. 사내 호스트명은 전체를 서비스한다.
var externalHosts = builder.Configuration.GetSection("Portal:ExternalHosts").Get<string[]>() ?? [];
if (externalHosts.Length > 0)
{
    var hostSet = new HashSet<string>(externalHosts.Select(h => h.Trim()), StringComparer.OrdinalIgnoreCase);
    app.Use(async (ctx, next) =>
    {
        if (hostSet.Contains(ctx.Request.Host.Host) && !AMES.Web.Services.PortalAuth.IsAllowedOnExternalHost(ctx.Request.Path))
        {
            if (ctx.Request.Path == "/") { ctx.Response.Redirect(AMES.Web.Services.PortalAuth.LoginPath); return; }
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        await next();
    });
}

app.UseAntiforgery();

// 외부 포탈 로그아웃 — 외부 쿠키만 지운다(내부 쿠키는 건드리지 않음)
app.MapGet("/portal/logout", async (HttpContext ctx) =>
{
    await Microsoft.AspNetCore.Authentication.AuthenticationHttpContextExtensions.SignOutAsync(ctx, AMES.Web.Services.PortalAuth.Scheme);
    return Results.Redirect(AMES.Web.Services.PortalAuth.LoginPath);
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

app.MapGet("/api/health", () => Results.Ok(new
    {
        status = "ok", at = DateTime.UtcNow,
        // 시각 보정 상태 — 화면의 Now/Today 가 DB 서버 시각과 맞는지 운영 중 확인용
        dbClock = new { now = DbClock.Now.ToString("yyyy-MM-dd HH:mm:ss"), offsetMinutes = Math.Round(DbClock.Offset.TotalMinutes, 1), synced = DbClock.IsSynced, syncAgeSec = DbClock.SyncAge is { } a ? (int?)a.TotalSeconds : null },
    }))
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
