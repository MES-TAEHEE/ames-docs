using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using AMES.Web.Components;
using AMES.Web.Components.Account;
using AMES.Web.Data;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using Radzen;

// EUC-KR(cp51949) — SRM 주간 구매계획(.xls=HTML) 디코딩용
System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddRadzenComponents();

builder.Services.AddLocalization();

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
        // Office workers create accounts via admin — no confirm-email flow.
        options.SignIn.RequireConfirmedAccount   = false;
        options.Password.RequireDigit            = true;
        options.Password.RequiredLength          = 6;
        options.Password.RequireNonAlphanumeric  = false;
        options.Password.RequireUppercase        = false;
        options.Password.RequireLowercase        = false;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();
builder.Services.AddHttpContextAccessor();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title       = "AMES Web API",
        Version     = "v1",
        Description = "AMES 사무실 포탈 유틸리티 엔드포인트. 인증은 쿠키 기반(ASP.NET Identity)입니다."
    });
});

// ── Shared data layer (reused from POP) ─────────────────────────────────
var factory = new AmesConnectionFactory(connectionString);
builder.Services.AddSingleton(factory);
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
builder.Services.AddSingleton(sp => new SysRepository(factory));
builder.Services.AddSingleton(sp => new AuthRepository(factory));
builder.Services.AddSingleton(sp => new LineScheduleRepository(factory));
builder.Services.AddSingleton(sp => new OeeRepository(factory));

var app = builder.Build();

// ── First-run admin seed ───────────────────────────────────────────────
// Ensures `admin@ames.local / Dev2026!` exists so a fresh checkout can
// sign in without a manual register step. No-op on subsequent boots.
using (var scope = app.Services.CreateScope())
{
    var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    const string email = "admin@ames.local";
    if (await userMgr.FindByEmailAsync(email) is null)
    {
        var u = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
        var res = await userMgr.CreateAsync(u, "Dev2026!");
        if (!res.Succeeded)
            app.Logger.LogWarning("admin seed failed: {Errs}", string.Join("; ", res.Errors.Select(e => e.Description)));
        else
            app.Logger.LogInformation("seeded admin@ames.local / Dev2026!");
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


var supportedCultures = new[] { "ko", "en" };
app.UseRequestLocalization(new RequestLocalizationOptions()
    .SetDefaultCulture("ko")
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures));

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
