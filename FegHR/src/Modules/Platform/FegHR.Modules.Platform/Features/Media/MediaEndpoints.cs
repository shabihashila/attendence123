using System.Text.RegularExpressions;
using FegHR.SharedKernel.Tenancy;
using FegHR.SharedServices.Clients;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace FegHR.Modules.Platform.Features.Media;

/// <summary>
/// Public delivery of a company's branding images, so an <c>&lt;img&gt;</c> tag can show them
/// without headers. The company comes from the key itself (<c>tenants/{id}/…</c>, the prefix
/// FileStorageClient writes) and only the branding folder is served; attendance photos, employee
/// photos and other private files are never reachable here. The response is
/// a redirect to a short-lived FileStorageService view URL, cached a little less than its lifetime.
/// </summary>
internal static partial class MediaEndpoints
{
    private const string Prefix = "/api/media/";
    private const int ViewUrlMinutes = 60;

    /// <summary>The public URL of a stored key, relative to the API base. Null stays null.</summary>
    public static string? UrlOf(string? key) => string.IsNullOrWhiteSpace(key) ? null : Prefix + key;

    public static void Map(IEndpointRouteBuilder routes)
    {
        routes.MapGet(Prefix + "{**key}", async (string key, IServiceScopeFactory scopes, IMemoryCache cache,
                HttpContext http, CancellationToken ct) =>
            {
                var match = PublicKey().Match(key ?? string.Empty);
                if (!match.Success || key!.Contains("..") || !int.TryParse(match.Groups["tenant"].Value, out var tenantId))
                    return Results.NotFound();

                var url = await cache.GetOrCreateAsync("media:" + key, async entry =>
                {
                    await using var scope = scopes.CreateTenantScope(tenantId);
                    var view = await scope.ServiceProvider.GetRequiredService<IFileStorage>().GetViewUrlAsync(key, ViewUrlMinutes, ct);
                    // A miss is cached briefly so a broken link cannot hammer the file service.
                    entry.AbsoluteExpiration = view is null ? DateTimeOffset.UtcNow.AddSeconds(30) : view.ExpiresAt.AddMinutes(-10);
                    return view?.Url;
                });
                if (url is null)
                    return Results.NotFound();
                http.Response.Headers.CacheControl = "public, max-age=600";
                return Results.Redirect(url);
            })
            .AllowAnonymous()
            .WithTags("Platform")
            .WithName("GetPublicMedia");
    }

    [GeneratedRegex(@"^tenants/(?<tenant>[1-9][0-9]{0,9})/(branding)/[A-Za-z0-9][A-Za-z0-9/_\-.]{0,300}$")]
    private static partial Regex PublicKey();
}
