using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using AMES.Web.Components;
using AMES.Web.Components.Account;
using AMES.Web.Data;
using AMES.Data.Connection;
using AMES.Data.Repositories;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

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

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();


app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

app.Run();
