using System.Security.Claims;
using FegHR.SharedKernel.Http;
using FegHR.SharedKernel.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog.Context;

namespace FegHR.SharedKernel.Tenancy;

/// <summary>
/// Binds <see cref="TenantContext"/> once per request. Runs after
/// authentication and before authorization.
/// <list type="bullet">
/// <item>Signed-in user: company from the <c>tid</c> claim, user from <c>sub</c>,
/// branch from <c>X-Branch-Id</c> (checked by <see cref="IBranchAccessPolicy"/>).</item>
/// <item>Anonymous public route: company from the host, or from the <c>X-Tenant</c> slug header
/// when <see cref="TenancyOptions.AllowTenantHeader"/> is on (development). Product-level public
/// routes (landing, registration, product branding) simply run unbound.</item>
/// </list>
/// Every request is also tagged in the log context with tenant, user and branch.
/// </summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next, IOptions<TenancyOptions> options)
{
    private readonly TenancyOptions _options = options.Value;

    public async Task InvokeAsync(HttpContext http, TenantContext tenant, IWebHostEnvironment environment)
    {
        var user = http.User;
        var isPublicRoute = http.Request.Path.StartsWithSegments(_options.PublicPathPrefix, StringComparison.OrdinalIgnoreCase);
        var audience = user.Identity?.IsAuthenticated == true ? user.FindFirstValue(ClaimNames.Audience) : null;

        int? tenantId = null, userId = null, branchId = null;
        var isPlatformAdmin = false;
        var source = TenantSource.None;

        if (isPublicRoute)
        {
            // A token is not honoured on public routes: those answer for the host's company, if any.
            tenantId = await ResolvePublicTenantAsync(http, environment);
            source = tenantId is null ? TenantSource.None
                : http.Items.ContainsKey(DevHeaderUsedKey) ? TenantSource.DevelopmentHeader : TenantSource.Host;
        }
        else if (audience == Audiences.Staff)
        {
            tenantId = ParseInt(user.FindFirstValue(ClaimNames.TenantId));
            userId = ParseInt(user.FindFirstValue(ClaimNames.Subject));
            isPlatformAdmin = user.FindFirstValue(ClaimNames.PlatformAdmin) == "1";
            branchId = ParseInt(http.Request.Headers[TenancyHeaders.Branch].ToString());
            source = TenantSource.StaffToken;
        }

        tenant.Bind(tenantId, userId, branchId, isPlatformAdmin, source);

        if (source == TenantSource.StaffToken && userId is { } uid && tenantId is { } tid
            && !http.Request.Path.StartsWithSegments("/api/iam/auth") && !http.Request.Path.StartsWithSegments("/api/iam/session"))
        {
            var access = http.RequestServices.GetRequiredService<ITenantAccessPolicy>();
            if (!await access.CanAccessAsync(uid, tid, isPlatformAdmin, http.RequestAborted))
            {
                await WriteFailure(http, StatusCodes.Status403Forbidden, "Your access to this company is no longer active. Switch company or sign in again.");
                return;
            }
            var requested = http.Request.Headers["X-Company-ID"].ToString();
            if (!string.IsNullOrEmpty(requested) && requested != tid.ToString())
            {
                await WriteFailure(http, StatusCodes.Status403Forbidden, "Switch company before requesting another company's data.");
                return;
            }
        }

        if (tenant.BranchId is { } requestedBranch && tenant.UserId is { } staffUserId && tenant.TenantId is > 0)
        {
            var policy = http.RequestServices.GetService<IBranchAccessPolicy>();
            if (policy is not null && !await policy.CanUseBranchAsync(staffUserId, requestedBranch, http.RequestAborted))
            {
                await WriteFailure(http, StatusCodes.Status403Forbidden, "You do not have access to this branch.");
                return;
            }
        }

        using (LogContext.PushProperty("TenantId", tenant.TenantId))
        using (LogContext.PushProperty("UserId", tenant.UserId))
        using (LogContext.PushProperty("BranchId", tenant.BranchId))
        {
            await next(http);
        }
    }

    private const string DevHeaderUsedKey = "FegHR.TenantFromHeader";

    private async Task<int?> ResolvePublicTenantAsync(HttpContext http, IWebHostEnvironment environment)
    {
        var resolver = http.RequestServices.GetService<ITenantHostResolver>();
        if (resolver is null)
            return null;

        var slug = http.Request.Headers[TenancyHeaders.Tenant].ToString();
        if (_options.AllowTenantHeader && environment.IsDevelopment() && !string.IsNullOrWhiteSpace(slug))
        {
            http.Items[DevHeaderUsedKey] = true;
            return await resolver.ResolveBySlugAsync(slug.Trim(), http.RequestAborted);
        }

        var host = http.Request.Host.Host;
        return string.IsNullOrWhiteSpace(host) ? null : await resolver.ResolveByHostAsync(host, http.RequestAborted);
    }

    private static int? ParseInt(string? value) => int.TryParse(value, out var parsed) && parsed > 0 ? parsed : null;

    private static Task WriteFailure(HttpContext http, int status, string message)
    {
        http.Response.StatusCode = status;
        return http.Response.WriteAsJsonAsync(ApiResponse.Fail(message), ApiJson.Options);
    }
}
