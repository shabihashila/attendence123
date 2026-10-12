using FegHR.SharedKernel.Http;
using FegHR.SharedKernel.Security;
using FegHR.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FegHR.Modules.Iam.Features.Auth;

internal static class AuthEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var auth = group.MapGroup("/auth").AllowAnonymous().SkipLicenseCheck();

        auth.MapPost("/login", async (LoginRequest request, AuthService service, CancellationToken ct) =>
                ToResult(await service.LoginAsync(request, ct)))
            .Validate<LoginRequest>()
            .RequireRateLimiting(RateLimitPolicies.Login)
            .WithName("Login");

        auth.MapPost("/select-tenant", async (SelectTenantRequest request, AuthService service, CancellationToken ct) =>
                ToResult(await service.SelectTenantAsync(request, ct)))
            .Validate<SelectTenantRequest>()
            .RequireRateLimiting(RateLimitPolicies.Login)
            .WithName("SelectTenant");

        auth.MapPost("/refresh", async (RefreshRequest request, AuthService service, CancellationToken ct) =>
                ToResult(await service.RefreshAsync(request, ct)))
            .Validate<RefreshRequest>()
            .WithName("RefreshToken");

        auth.MapPost("/logout", async (RefreshRequest request, AuthService service, CancellationToken ct) =>
            {
                await service.LogoutAsync(request, ct);
                return ApiResults.Ok("Signed out.");
            })
            .Validate<RefreshRequest>()
            .WithName("Logout");

        // Signed-in: the top-bar company switcher.
        var session = group.MapGroup("/session").SkipLicenseCheck();

        session.MapGet("/tenants", async (ITenantContext tenant, AuthService service, CancellationToken ct) =>
                ApiResults.Ok(await service.GetTenantOptionsAsync(tenant.UserId!.Value, ct)))
            .RequireStaff()
            .WithName("ListMyTenants");

        session.MapPost("/switch-tenant", async (SwitchTenantRequest request, ITenantContext tenant, AuthService service,
                CancellationToken ct) => ToResult(await service.SwitchTenantAsync(tenant.UserId!.Value, request, ct)))
            .Validate<SwitchTenantRequest>()
            .RequireStaff()
            .RequireRateLimiting(RateLimitPolicies.Login)
            .WithName("SwitchTenant");
    }

    private static IResult ToResult<T>(AuthOutcome<T> outcome)
        => outcome.Succeeded ? ApiResults.Ok(outcome.Data!) : ApiResults.Fail(outcome.Error!, outcome.Status);
}
