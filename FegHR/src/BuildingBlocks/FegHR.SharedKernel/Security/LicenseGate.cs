using FegHR.SharedKernel.Http;
using FegHR.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace FegHR.SharedKernel.Security;

/// <summary>Whether the bound company may use the product right now. Implemented by the Platform module.</summary>
public interface ILicenseGate
{
    ValueTask<LicenseVerdict> CheckAsync(int tenantId, CancellationToken ct = default);
}

/// <param name="IsLicensed">A licence covers this moment and the company is active.</param>
/// <param name="Message">What to tell the user when it does not.</param>
public sealed record LicenseVerdict(bool IsLicensed, string? Message, DateTimeOffset? EndsAt);

/// <summary>Endpoint metadata: reachable without a licence (sign-in, the licence page, the product owner console).</summary>
public sealed class SkipLicenseCheckAttribute : Attribute;

/// <summary>
/// Refuses company work when the company's licence has lapsed, the way DailyCommerce's licence
/// guard does: HTTP 402 with the response envelope, so the UI can send the user to the licence
/// page. Runs after the tenant is bound. Skipped for anonymous requests, endpoints marked
/// <see cref="SkipLicenseCheckAttribute"/> and the product owner, who must be able to get in
/// and fix things.
/// </summary>
public sealed class LicenseGuardMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, ITenantContext tenant)
    {
        var endpoint = http.GetEndpoint();
        if (endpoint is null || endpoint.Metadata.GetMetadata<SkipLicenseCheckAttribute>() is not null
            || tenant.Source != TenantSource.StaffToken || tenant.TenantId is not { } tenantId || tenant.IsPlatformAdmin)
        {
            await next(http);
            return;
        }

        var gate = http.RequestServices.GetService<ILicenseGate>();
        var verdict = gate is null ? null : await gate.CheckAsync(tenantId, http.RequestAborted);
        if (verdict is null || verdict.IsLicensed)
        {
            await next(http);
            return;
        }

        Log.ForContext("SourceContext", typeof(LicenseGuardMiddleware).FullName)
            .Warning("Licence denial for tenant {TenantId} on {Path}", tenantId, http.Request.Path.Value);
        http.Response.StatusCode = StatusCodes.Status402PaymentRequired;
        await http.Response.WriteAsJsonAsync(
            ApiResponse.Fail(verdict.Message ?? "Your licence has expired. Please contact us to continue."), ApiJson.Options);
    }
}

public static class LicenseExtensions
{
    /// <summary>Marks an endpoint or group as reachable without a licence.</summary>
    public static TBuilder SkipLicenseCheck<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
        => builder.WithMetadata(new SkipLicenseCheckAttribute());
}
