using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using FegHR.Modules.Platform.Domain;
using FegHR.Modules.Platform.Persistence;
using FegHR.SharedKernel.Http;
using FegHR.SharedKernel.Persistence;
using FegHR.SharedKernel.Security;
using FegHR.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using Shared.Time;

namespace FegHR.Modules.Platform.Features.Branding;

/// <summary>What the landing, registration and sign-in pages render before anyone is signed in.</summary>
internal sealed record PlatformBrandingDto(
    string BrandName,
    IReadOnlyList<NameSegment>? NameStyle,
    string? Tagline,
    string PrimaryColor,
    string AccentColor,
    string? SupportEmail,
    string? SupportPhone,
    string? FooterText,
    string? LogoUrl,
    string? FaviconUrl,
    string RowVersion);

internal sealed record SavePlatformBrandingRequest(
    [property: Required, StringLength(60, MinimumLength = 1)] string BrandName,
    IReadOnlyList<NameSegment>? NameStyle,
    [property: StringLength(200)] string? Tagline,
    [property: Required, RegularExpression("^#[0-9a-fA-F]{6}$", ErrorMessage = "Use a colour like #10B981.")] string PrimaryColor,
    [property: Required, RegularExpression("^#[0-9a-fA-F]{6}$", ErrorMessage = "Use a colour like #0F766E.")] string AccentColor,
    [property: StringLength(254), EmailAddress] string? SupportEmail,
    [property: StringLength(30)] string? SupportPhone,
    [property: StringLength(300)] string? FooterText,
    [property: Required] string RowVersion);

/// <summary>
/// Product branding, edited by the product owner like DailyCommerce's platform branding: a brand
/// name with per-word styling, a logo and a separate favicon, colours and contact lines. The
/// images are stored in the row, so the public pages need no storage service.
/// </summary>
internal static class PlatformBrandingEndpoints
{
    private const long MaxLogoBytes = 1024 * 1024;
    private const long MaxFaviconBytes = 256 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void Map(IEndpointRouteBuilder routes)
    {
        var open = routes.MapGroup("/api/public/platform-branding").AllowAnonymous().WithTags("Platform");

        open.MapGet("/", async (PlatformDbContext db, CancellationToken ct) =>
                ApiResults.Ok(ToDto(await LoadAsync(db, track: false, ct)) with { RowVersion = string.Empty }))
            .WithName("GetPlatformBranding");

        open.MapGet("/{slot}", async (string slot, PlatformDbContext db, HttpContext http, CancellationToken ct) =>
            {
                if (slot is not ("logo" or "favicon"))
                    return Results.NotFound();
                var row = await LoadAsync(db, track: false, ct);
                var (bytes, type) = slot == "logo" ? (row.LogoBytes, row.LogoContentType) : (row.FaviconBytes, row.FaviconContentType);
                if (bytes is null || type is null)
                    return Results.NotFound();
                // The URL carries the row version, so a saved change is a new URL.
                http.Response.Headers.CacheControl = "public, max-age=86400";
                http.Response.Headers.XContentTypeOptions = "nosniff";
                return Results.File(bytes, type, entityTag: new EntityTagHeaderValue($"\"{Convert.ToHexString(row.RowVersion)}\""));
            })
            .WithName("GetPlatformBrandingImage");

        var owner = routes.MapGroup("/api/owner/branding").WithTags("Owner").RequirePlatformAdmin().SkipLicenseCheck();

        owner.MapGet("/", async (PlatformDbContext db, CancellationToken ct) =>
                ApiResults.Ok(ToDto(await LoadAsync(db, track: false, ct))))
            .WithName("GetOwnerBranding");

        owner.MapPut("/", async (SavePlatformBrandingRequest request, PlatformDbContext db, ITenantContext tenant,
                IAppTimeProvider clock, CancellationToken ct) =>
            {
                if (BrandingEndpoints.RunsProblem(request.NameStyle, 60) is { } problem)
                    return ApiResults.Fail(problem);
                if (request.BrandName.Any(char.IsControl) || (request.Tagline ?? string.Empty).Any(char.IsControl)
                    || (request.FooterText ?? string.Empty).Any(char.IsControl))
                    return ApiResults.Fail("Text fields cannot contain control characters.");

                var row = await LoadAsync(db, track: true, ct);
                if (!RowVersions.TryApply(db, row, request.RowVersion))
                    return ApiResults.Fail("The row version is missing or invalid.");
                var styled = BrandingEndpoints.Styled(request.NameStyle);
                // A formatted name is the source of truth for the plain name.
                row.BrandName = styled is not null ? string.Concat(styled.Select(x => x.Text)).Trim() : request.BrandName.Trim();
                row.NameStyleJson = JsonSerializer.Serialize(styled ?? [], Json);
                row.Tagline = BrandingEndpoints.Clean(request.Tagline);
                row.PrimaryColour = request.PrimaryColor.ToUpperInvariant();
                row.AccentColour = request.AccentColor.ToUpperInvariant();
                row.SupportEmail = BrandingEndpoints.Clean(request.SupportEmail);
                row.SupportPhone = BrandingEndpoints.Clean(request.SupportPhone);
                row.FooterText = BrandingEndpoints.Clean(request.FooterText);
                Stamp(row, tenant, clock);
                await db.SaveChangesAsync(ct);
                return ApiResults.Ok(ToDto(row), "Product branding saved.");
            })
            .Validate<SavePlatformBrandingRequest>()
            .WithName("SaveOwnerBranding");

        owner.MapPost("/{slot}", async (string slot, IFormFile file, PlatformDbContext db, ITenantContext tenant,
                IAppTimeProvider clock, CancellationToken ct) =>
            {
                if (slot is not ("logo" or "favicon"))
                    return ApiResults.NotFound("The image slot");
                var limit = slot == "logo" ? MaxLogoBytes : MaxFaviconBytes;
                if (file.Length == 0 || file.Length > limit)
                    return ApiResults.Fail($"Upload an image of up to {limit / 1024} KB.");

                // The browser's content type is a claim; the bytes decide.
                using var buffer = new MemoryStream();
                await file.CopyToAsync(buffer, ct);
                var bytes = buffer.ToArray();
                var type = BrandingEndpoints.ImageType(bytes)?.ContentType ?? (slot == "favicon" && IsIcon(bytes) ? "image/x-icon" : null);
                if (type is null)
                    return ApiResults.Fail(slot == "logo" ? "Upload a PNG, JPEG or WebP image." : "Upload a PNG, ICO or WebP icon.");
                if (slot == "favicon" && type == "image/jpeg")
                    return ApiResults.Fail("Upload a PNG, ICO or WebP icon.");

                var row = await LoadAsync(db, track: true, ct);
                if (slot == "logo") { row.LogoBytes = bytes; row.LogoContentType = type; }
                else { row.FaviconBytes = bytes; row.FaviconContentType = type; }
                Stamp(row, tenant, clock);
                await db.SaveChangesAsync(ct);
                return ApiResults.Ok(ToDto(row), "Image saved.");
            })
            .DisableAntiforgery()
            .WithName("UploadOwnerBrandingImage");

        owner.MapDelete("/{slot}", async (string slot, PlatformDbContext db, ITenantContext tenant, IAppTimeProvider clock,
                CancellationToken ct) =>
            {
                if (slot is not ("logo" or "favicon"))
                    return ApiResults.NotFound("The image slot");
                var row = await LoadAsync(db, track: true, ct);
                if (slot == "logo") { row.LogoBytes = null; row.LogoContentType = null; }
                else { row.FaviconBytes = null; row.FaviconContentType = null; }
                Stamp(row, tenant, clock);
                await db.SaveChangesAsync(ct);
                return ApiResults.Ok(ToDto(row), "Image removed.");
            })
            .WithName("RemoveOwnerBrandingImage");
    }

    private static async Task<PlatformBranding> LoadAsync(PlatformDbContext db, bool track, CancellationToken ct)
    {
        var query = track ? db.PlatformBranding : db.PlatformBranding.AsNoTracking();
        return await query.SingleOrDefaultAsync(x => x.Id == 1, ct)
            ?? throw new InvalidOperationException("platform.PlatformBranding has no row; redeploy the database scripts.");
    }

    private static void Stamp(PlatformBranding row, ITenantContext tenant, IAppTimeProvider clock)
    {
        row.UpdatedAt = clock.UtcNowOffset();
        row.UpdatedBy = tenant.UserId;
    }

    private static PlatformBrandingDto ToDto(PlatformBranding row)
    {
        IReadOnlyList<NameSegment>? runs;
        try { runs = JsonSerializer.Deserialize<List<NameSegment>>(row.NameStyleJson, Json); }
        catch (JsonException) { runs = null; }
        var version = Convert.ToHexString(row.RowVersion);
        return new PlatformBrandingDto(
            row.BrandName,
            runs is { Count: > 0 } ? runs : null,
            row.Tagline,
            row.PrimaryColour ?? BrandingEndpoints.DefaultPrimary,
            row.AccentColour ?? BrandingEndpoints.DefaultAccent,
            row.SupportEmail, row.SupportPhone, row.FooterText,
            row.LogoBytes is null ? null : $"/api/public/platform-branding/logo?v={version}",
            row.FaviconBytes is null ? null : $"/api/public/platform-branding/favicon?v={version}",
            Convert.ToBase64String(row.RowVersion));
    }

    private static bool IsIcon(ReadOnlySpan<byte> data)
        => data.Length >= 22 && data[0] == 0 && data[1] == 0 && data[2] == 1 && data[3] == 0;
}
