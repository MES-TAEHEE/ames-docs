using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using AMES.Web.Data;

namespace Microsoft.AspNetCore.Routing;

internal static class IdentityComponentsEndpointRouteBuilderExtensions
{
    // These endpoints are required by the Identity Razor components defined in the /Components/Account/Pages directory of this project.
    public static IEndpointConventionBuilder MapAdditionalIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var accountGroup = endpoints.MapGroup("/Account");

        accountGroup.MapPost("/Logout", async (
            ClaimsPrincipal user,
            [FromServices] SignInManager<ApplicationUser> signInManager,
            [FromForm] string returnUrl) =>
        {
            await signInManager.SignOutAsync();
            return TypedResults.LocalRedirect($"~/{returnUrl}");
        });

        // 상단바 비밀번호 변경 직후 — 회로가 발급한 1회용 표로 새 로그인 쿠키를 준다(AMES.Web.Services.SignInRefreshTickets).
        // 기존 쿠키는 스탬프가 바뀌어 거부될 수 있으므로 현재 인증에 기대지 않는다. ACTIVE 계정만, 로그인 유지(IsPersistent)는 기존 쿠키 값을 따른다.
        accountGroup.MapGet("/RefreshSignIn", async (
            HttpContext ctx,
            [FromQuery(Name = "t")] string? token,
            [FromQuery] string? returnUrl,
            [FromServices] AMES.Web.Services.SignInRefreshTickets tickets,
            [FromServices] UserManager<ApplicationUser> userManager,
            [FromServices] SignInManager<ApplicationUser> signInManager,
            [FromServices] AMES.Data.Repositories.AuthRepository authRepo) =>
        {
            var userId = tickets.Redeem(token);
            var user = userId is null ? null : await userManager.FindByIdAsync(userId);
            if (user is null || !string.Equals(authRepo.GetProfileStatus(user.Id).AccountStatus, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                return Results.LocalRedirect("~/Account/Login");
            var current = await ctx.AuthenticateAsync(IdentityConstants.ApplicationScheme);
            await signInManager.SignInAsync(user, isPersistent: current.Properties?.IsPersistent ?? false);
            return Results.LocalRedirect(AMES.Web.Services.LocalUrl.OrDefault(returnUrl, "/"));
        }).AllowAnonymous();

        var manageGroup = accountGroup.MapGroup("/Manage").RequireAuthorization();

        var loggerFactory = endpoints.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var downloadLogger = loggerFactory.CreateLogger("DownloadPersonalData");

        manageGroup.MapPost("/DownloadPersonalData", async (
            HttpContext context,
            [FromServices] UserManager<ApplicationUser> userManager,
            [FromServices] AuthenticationStateProvider authenticationStateProvider) =>
        {
            var user = await userManager.GetUserAsync(context.User);
            if (user is null)
            {
                return Results.NotFound($"Unable to load user with ID '{userManager.GetUserId(context.User)}'.");
            }

            var userId = await userManager.GetUserIdAsync(user);
            downloadLogger.LogInformation("User with ID '{UserId}' asked for their personal data.", userId);

            // Only include personal data for download
            var personalData = new Dictionary<string, string>();
            var personalDataProps = typeof(ApplicationUser).GetProperties().Where(
                prop => Attribute.IsDefined(prop, typeof(PersonalDataAttribute)));
            foreach (var p in personalDataProps)
            {
                personalData.Add(p.Name, p.GetValue(user)?.ToString() ?? "null");
            }

            var logins = await userManager.GetLoginsAsync(user);
            foreach (var l in logins)
            {
                personalData.Add($"{l.LoginProvider} external login provider key", l.ProviderKey);
            }

            var fileBytes = JsonSerializer.SerializeToUtf8Bytes(personalData);

            context.Response.Headers.TryAdd("Content-Disposition", "attachment; filename=PersonalData.json");
            return TypedResults.File(fileBytes, contentType: "application/json", fileDownloadName: "PersonalData.json");
        });

        return accountGroup;
    }
}
