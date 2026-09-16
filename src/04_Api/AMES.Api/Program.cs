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
builder.Services.AddSingleton(new WorkerRepository(factory));
builder.Services.AddSingleton(new PopSessionRepository(factory));
builder.Services.AddSingleton(sp => new PopAuthService(
    sp.GetRequiredService<AuthRepository>(),
    sp.GetRequiredService<WorkerRepository>(),
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
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "AMES API",
        Version = "v1",
        Description = "PDA, Tablet, authentication and shared AMES endpoints. Sign in with POST /api/auth/login, then enter the returned token in Authorize."
    });
    // 엔드포인트 파일마다 같은 이름의 중첩 요청 레코드(예: Wh/Fg 의 AdjustSaveReq)가 있어
    // 기본 schemaId(타입 단순명)로는 충돌해 swagger.json 이 500 을 낸다 — 선언 타입까지 포함한 이름을 쓴다.
    c.CustomSchemaIds(SwaggerSchemaId);
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
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "AMES API v1");
    c.RoutePrefix = "swagger";
    c.EnablePersistAuthorization();
    c.DisplayRequestDuration();
});

// ── Endpoints ───────────────────────────────────────────────────────────
app.MapGet("/api/health", () => Results.Ok(new { status = "ok", at = DateTime.UtcNow }));
app.MapAuth(app.Services.GetRequiredService<PopAuthService>(), tokens);
app.MapWh(factory);
app.MapFg(factory);
app.MapTablet(factory);
app.MapPp(factory);
app.MapMnt(factory);
app.MapRpt(factory);
app.MapSys(factory);

app.Run();

// Swagger schemaId: 중첩 레코드는 선언 타입까지, 제너릭은 인자 이름까지 포함해 유일하게 만든다.
static string SwaggerSchemaId(Type t)
{
    if (t.IsGenericType)
    {
        var bare = t.Name[..t.Name.IndexOf('`')];
        return bare + "Of" + string.Join("And", t.GetGenericArguments().Select(SwaggerSchemaId));
    }
    return (t.FullName ?? t.Name)
        .Replace("AMES.Api.Endpoints.", "")
        .Replace("AMES.Contracts.Dto.", "")
        .Replace('+', '.');
}
