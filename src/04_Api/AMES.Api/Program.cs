using AMES.Api.Auth;
using AMES.Api.Endpoints;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using AMES.Data.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Connection + repositories ───────────────────────────────────────────
var cs = builder.Configuration.GetConnectionString("AMES")
         ?? throw new InvalidOperationException("ConnectionStrings:AMES missing in appsettings.json");
var factory = new AmesConnectionFactory(cs);
builder.Services.AddSingleton(factory);
builder.Services.AddSingleton(new AuthRepository(factory));
builder.Services.AddSingleton(new PopSessionRepository(factory));
builder.Services.AddSingleton(sp => new PopAuthService(
    sp.GetRequiredService<AuthRepository>(),
    sp.GetRequiredService<PopSessionRepository>()));

// ── Auth token registry ─────────────────────────────────────────────────
var tokens = new TokenStore();
builder.Services.AddSingleton(tokens);

// ── CORS — PDA clients come from arbitrary devices on the LAN ───────────
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo { Title = "AMES API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name        = "Authorization",
        Type        = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme      = "bearer",
        Description = "POST /api/auth/login 으로 발급받은 토큰을 입력하세요."
    });
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id   = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

app.UseCors();
app.UseBearerAuth(tokens);

app.UseSwagger();
app.UseSwaggerUI();

// ── Endpoints ───────────────────────────────────────────────────────────
app.MapGet("/api/health", () => Results.Ok(new { status = "ok", at = DateTime.UtcNow }));
app.MapAuth(app.Services.GetRequiredService<PopAuthService>(), tokens);
app.MapWh(factory);
app.MapFg(factory);
app.MapPp(factory);
app.MapMnt(factory);
app.MapRpt(factory);
app.MapSys(factory);

app.Run();
