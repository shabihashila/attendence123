using FegHR.Modules.Platform.Contracts;
using FegHR.SharedKernel.Http;
using FegHR.SharedKernel.Security;
using FegHR.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FegHR.Modules.Platform.Features.Tenant;

internal static class TenantEndpoints
{
    public static void Map(RouteGroupBuilder staff, IEndpointRouteBuilder routes)
    {
        // The signed-in user's company, for the shell header.
        staff.MapGet("/tenant", async (ITenantDirectory tenants, CancellationToken ct) =>
                await tenants.GetCurrentAsync(ct) is { } tenant ? ApiResults.Ok(tenant) : ApiResults.NotFound("The company"))
            .RequireStaff()
            .SkipLicenseCheck()
            .WithName("GetCurrentTenant");

        // The company a host belongs to (or X-Tenant in development), for a company-branded sign-in page.
        routes.MapGet("/api/public/tenant", async (ITenantContext context, ITenantDirectory tenants, CancellationToken ct) =>
                context.TenantId is > 0 && await tenants.GetCurrentAsync(ct) is { } tenant
                    ? ApiResults.Ok(tenant)
                    : ApiResults.NotFound("The company"))
            .AllowAnonymous()
            .WithTags("Platform")
            .WithName("GetPublicTenant");
    }
}
