using FegHR.SharedKernel.Http;
using FegHR.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace FegHR.SharedKernel.Security;

/// <summary>
/// The caller's effective permission codes in the bound tenant. Implemented by
/// the Iam module, cached per tenant and user, and evicted when roles change.
/// </summary>
public interface IPermissionProvider
{
    ValueTask<IReadOnlySet<string>> GetPermissionsAsync(int tenantId, int userId, CancellationToken ct = default);
}

/// <summary>
/// Endpoint filter behind <c>.RequirePermission(...)</c>. The caller needs at
/// least one of the listed codes. There is no platform-admin bypass: tenant
/// permissions belong to the tenant.
/// </summary>
internal sealed class PermissionEndpointFilter(string[] anyOf) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var tenant = http.RequestServices.GetRequiredService<ITenantContext>();

        if (tenant.TenantId is not { } tenantId || tenant.UserId is not { } userId)
            return ApiResults.Fail("Sign in to continue.", StatusCodes.Status401Unauthorized);

        var provider = http.RequestServices.GetRequiredService<IPermissionProvider>();
        var granted = await provider.GetPermissionsAsync(tenantId, userId, http.RequestAborted);

        if (anyOf.Any(granted.Contains))
            return await next(context);

        Log.ForContext("SourceContext", typeof(PermissionEndpointFilter).FullName)
            .Warning("Permission denied: user {UserId} tenant {TenantId} needs one of {Permissions} for {Path}",
                userId, tenantId, anyOf, http.Request.Path.Value);
        return ApiResults.Fail("You do not have permission to do this.", StatusCodes.Status403Forbidden);
    }
}

public static class AuthorizationExtensions
{
    /// <summary>Any signed-in user of the bound company.</summary>
    public static TBuilder RequireStaff<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
        => builder.RequireAuthorization(PolicyNames.Staff);

    /// <summary>Product owner endpoints (companies, licences, product branding).</summary>
    public static TBuilder RequirePlatformAdmin<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
        => builder.RequireAuthorization(PolicyNames.PlatformAdmin);

    /// <summary>Staff user holding at least one of <paramref name="anyOf"/> in the bound tenant.</summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, params string[] anyOf)
        where TBuilder : IEndpointConventionBuilder
    {
        if (anyOf.Length == 0)
            throw new ArgumentException("At least one permission code is required.", nameof(anyOf));
        builder.RequireAuthorization(PolicyNames.Staff);
        builder.AddEndpointFilter(new PermissionEndpointFilter(anyOf));
        return builder;
    }
}
