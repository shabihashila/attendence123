using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FegHR.Modules.Platform.Contracts;
using FegHR.Modules.Platform.Domain;
using FegHR.Modules.Platform.Persistence;
using FegHR.SharedKernel.Http;
using FegHR.SharedKernel.Persistence;
using FegHR.SharedKernel.Security;
using FegHR.SharedKernel.Tenancy;
using FegHR.SharedServices.Clients;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace FegHR.Modules.Platform.Features.Branding;

/// <summary>
/// Stored in <c>TenantSettings.BrandingJson</c> (version 1). Images are FileStorage keys
/// confined to the company; colours are plain hex values. Nothing here is rendered as markup.
/// </summary>
internal sealed record BrandingPayload(
    [property: JsonPropertyName("version")] int V = 1,
    string? DisplayName = null,
    string? Tagline = null,
    [property: JsonPropertyName("logoAssetKey")] string? LogoKey = null,
    [property: JsonPropertyName("heroAssetKey")] string? HeroImageKey = null,
    [property: JsonPropertyName("primaryColour")] string? PrimaryColor = null,
    [property: JsonPropertyName("accentColour")] string? AccentColor = null,
    string? Phone = null,
    string? Email = null,
    string? Address = null,
    string? FacebookUrl = null,
    string? InstagramUrl = null,
    IReadOnlyList<NameSegment>? NameStyle = null);

/// <summary>
/// One styled run of the company's name, like DailyCommerce's formatted brand name but as data:
/// no markup is stored or rendered. The runs' text, joined, is the display name.
/// <see cref="Font"/> is a key of <see cref="BrandingEndpoints.Fonts"/>, not a CSS value.
/// </summary>
internal sealed record NameSegment(
    [property: Required, StringLength(80, MinimumLength = 1)] string Text,
    string? Color = null,
    string? Background = null,
    bool Bold = false,
    bool Italic = false,
    bool Underline = false,
    string? Font = null,
    bool Strike = false,
    [property: Range(8, 72)] int? FontSize = null);

/// <summary>What the application shell and the branding page render.</summary>
internal sealed record BrandingDto(
    string Name,
    string? Tagline,
    string? LogoUrl,
    string? HeroImageUrl,
    string PrimaryColor,
    string AccentColor,
    string? Phone,
    string? Email,
    string? Address,
    string? FacebookUrl,
    string? InstagramUrl,
    string? DisplayName,
    string RowVersion,
    IReadOnlyList<NameSegment>? NameStyle = null);

internal sealed record SaveBrandingRequest(
    [property: StringLength(80)] string? DisplayName,
    [property: StringLength(160)] string? Tagline,
    [property: Required, RegularExpression("^#[0-9a-fA-F]{6}$", ErrorMessage = "Use a colour like #10B981.")] string PrimaryColor,
    [property: Required, RegularExpression("^#[0-9a-fA-F]{6}$", ErrorMessage = "Use a colour like #0F766E.")] string AccentColor,
    [property: StringLength(30)] string? Phone,
    [property: StringLength(254), EmailAddress] string? Email,
    [property: StringLength(300)] string? Address,
    [property: StringLength(300)] string? FacebookUrl,
    [property: StringLength(300)] string? InstagramUrl,
    [property: Required] string RowVersion,
    IReadOnlyList<NameSegment>? NameStyle = null);

internal static partial class BrandingEndpoints
{
    public const string DefaultPrimary = "#1863DC";
    public const string DefaultAccent = "#0F9F8F";
    private const long MaxImageBytes = 2 * 1024 * 1024;
    /// <summary>Font keys a name run may use; the UIs map each to a font stack they ship.</summary>
    public static readonly IReadOnlySet<string> Fonts = new HashSet<string>(StringComparer.Ordinal)
        { "sans", "display", "serif", "script", "mono" };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void Map(RouteGroupBuilder staff, IEndpointRouteBuilder routes)
    {
        var branding = staff.MapGroup("/branding");

        // Every signed-in user: the shell shows the company's name, logo and colours.
        branding.MapGet("/", async (PlatformDbContext db, ITenantDirectory tenants, CancellationToken ct) =>
                await LoadAsync(db, tenants, ct) is { } dto ? ApiResults.Ok(dto) : ApiResults.NotFound("The company settings"))
            .RequireStaff()
            .SkipLicenseCheck()
            .WithName("GetBranding");

        branding.MapPut("/", async (SaveBrandingRequest request, PlatformDbContext db, ITenantDirectory tenants,
                CancellationToken ct) =>
            {
                var row = await db.TenantSettings.SingleOrDefaultAsync(ct);
                if (row is null)
                    return ApiResults.NotFound("The company settings");
                if (!RowVersions.TryApply(db, row, request.RowVersion))
                    return ApiResults.Fail("The row version is missing or invalid.");
                if (Problem(request) is { } problem)
                    return ApiResults.Fail(problem);

                var current = Read(row);
                row.BrandingJson = JsonSerializer.Serialize(current with
                {
                    // A formatted name is the source of truth for the plain name, as in DailyCommerce.
                    DisplayName = Styled(request.NameStyle) is { } styled ? string.Concat(styled.Select(x => x.Text)).Trim() : Clean(request.DisplayName),
                    NameStyle = Styled(request.NameStyle),
                    Tagline = Clean(request.Tagline),
                    PrimaryColor = request.PrimaryColor.ToUpperInvariant(),
                    AccentColor = request.AccentColor.ToUpperInvariant(),
                    Phone = Clean(request.Phone),
                    Email = Clean(request.Email),
                    Address = Clean(request.Address),
                    FacebookUrl = Clean(request.FacebookUrl),
                    InstagramUrl = Clean(request.InstagramUrl)
                }, Json);
                try { await db.SaveChangesAsync(ct); }
                catch (DbUpdateConcurrencyException) { return ApiResults.Conflict(); }
                return ApiResults.Ok((await LoadAsync(db, tenants, ct))!, "Branding saved.");
            })
            .Validate<SaveBrandingRequest>()
            .RequirePermission(PlatformPermissions.SettingsWrite)
            .WithName("SaveBranding");

        // Logo and hero image through the shared FileStorageService. JWT-only API, so no antiforgery.
        branding.MapPost("/{slot}", async (string slot, IFormFile file, PlatformDbContext db, ITenantDirectory tenants,
                IFileStorage storage, CancellationToken ct) =>
            {
                if (slot is not ("logo" or "hero"))
                    return ApiResults.NotFound("The image slot");
                if (file.Length is 0 or > MaxImageBytes)
                    return ApiResults.Fail("Upload an image of up to 2 MB.");

                // The browser's content type is a claim; the bytes decide.
                using var buffer = new MemoryStream();
                await file.CopyToAsync(buffer, ct);
                if (ImageType(buffer.GetBuffer().AsSpan(0, (int)buffer.Length)) is not { } type)
                    return ApiResults.Fail("Upload a PNG, JPEG or WebP image.");

                var row = await db.TenantSettings.SingleOrDefaultAsync(ct);
                if (row is null)
                    return ApiResults.NotFound("The company settings");
                buffer.Position = 0;
                var stored = await storage.UploadAsync($"branding/{slot}-{Guid.NewGuid():N}{type.Extension}", buffer, type.ContentType, ct);
                if (!stored.Success)
                    return ApiResults.Fail(stored.Error ?? "The image could not be stored.", StatusCodes.Status502BadGateway);

                var current = Read(row);
                var previous = slot == "logo" ? current.LogoKey : current.HeroImageKey;
                row.BrandingJson = JsonSerializer.Serialize(slot == "logo"
                    ? current with { LogoKey = stored.Key }
                    : current with { HeroImageKey = stored.Key }, Json);
                try { await db.SaveChangesAsync(ct); }
                catch (DbUpdateConcurrencyException)
                {
                    await storage.DeleteAsync(stored.Key, ct);
                    return ApiResults.Conflict();
                }
                if (previous is not null)
                    await storage.DeleteAsync(previous, ct);
                return ApiResults.Ok((await LoadAsync(db, tenants, ct))!, "Image saved.");
            })
            .DisableAntiforgery()
            .RequirePermission(PlatformPermissions.SettingsWrite)
            .WithName("UploadBrandingImage");

        branding.MapDelete("/{slot}", async (string slot, PlatformDbContext db, ITenantDirectory tenants,
                IFileStorage storage, CancellationToken ct) =>
            {
                if (slot is not ("logo" or "hero"))
                    return ApiResults.NotFound("The image slot");
                var row = await db.TenantSettings.SingleOrDefaultAsync(ct);
                if (row is null)
                    return ApiResults.NotFound("The company settings");
                var current = Read(row);
                var previous = slot == "logo" ? current.LogoKey : current.HeroImageKey;
                row.BrandingJson = JsonSerializer.Serialize(slot == "logo"
                    ? current with { LogoKey = null }
                    : current with { HeroImageKey = null }, Json);
                await db.SaveChangesAsync(ct);
                if (previous is not null)
                    await storage.DeleteAsync(previous, ct);
                return ApiResults.Ok((await LoadAsync(db, tenants, ct))!, "Image removed.");
            })
            .RequirePermission(PlatformPermissions.SettingsWrite)
            .WithName("RemoveBrandingImage");

        // A company's branding for a sign-in page on its own host (or X-Tenant in development).
        routes.MapGet("/api/public/branding", async (ITenantContext context, PlatformDbContext db, ITenantDirectory tenants,
                CancellationToken ct) =>
                context.TenantId is > 0 && await LoadAsync(db, tenants, ct) is { } dto
                    ? ApiResults.Ok(dto with { RowVersion = string.Empty })
                    : ApiResults.NotFound("The company"))
            .AllowAnonymous()
            .WithTags("Platform")
            .WithName("GetPublicBranding");
    }

    private static async Task<BrandingDto?> LoadAsync(PlatformDbContext db, ITenantDirectory tenants, CancellationToken ct)
    {
        var row = await db.TenantSettings.AsNoTracking().SingleOrDefaultAsync(ct);
        var tenant = await tenants.GetCurrentAsync(ct);
        if (row is null || tenant is null)
            return null;
        var b = Read(row);
        return new BrandingDto(
            b.DisplayName ?? tenant.Name,
            b.Tagline,
            Media.MediaEndpoints.UrlOf(b.LogoKey),
            Media.MediaEndpoints.UrlOf(b.HeroImageKey),
            b.PrimaryColor ?? DefaultPrimary,
            b.AccentColor ?? DefaultAccent,
            b.Phone, b.Email, b.Address, b.FacebookUrl, b.InstagramUrl,
            b.DisplayName,
            Convert.ToBase64String(row.RowVersion),
            b.NameStyle);
    }

    private static BrandingPayload Read(TenantSetting row)
    {
        try { return JsonSerializer.Deserialize<BrandingPayload>(row.BrandingJson, Json) ?? new BrandingPayload(); }
        catch (JsonException) { return new BrandingPayload(); }
    }

    /// <summary>Null when the name is plain (no runs, or one run without any styling).</summary>
    internal static IReadOnlyList<NameSegment>? Styled(IReadOnlyList<NameSegment>? runs)
    {
        if (runs is null || runs.Count == 0)
            return null;
        var clean = runs.Select(r => r with
        {
            Color = r.Color?.ToUpperInvariant(),
            Background = r.Background?.ToUpperInvariant(),
            Font = string.IsNullOrWhiteSpace(r.Font) ? null : r.Font
        }).ToList();
        return clean.Count == 1 && clean[0] is { Color: null, Background: null, Bold: false, Italic: false, Underline: false, Font: null, Strike: false, FontSize: null }
            ? null
            : clean;
    }

    /// <summary>Null when the styled runs are acceptable; otherwise the message to show.</summary>
    internal static string? RunsProblem(IReadOnlyList<NameSegment>? runs, int maxLength)
    {
        if (runs is not { Count: > 0 })
            return null;
        if (runs.Count > 30)
            return "The formatted name has too many styled parts.";
        var text = string.Concat(runs.Select(x => x.Text ?? string.Empty));
        if (text.Trim().Length < 1 || text.Trim().Length > maxLength)
            return $"Enter a name of 1–{maxLength} characters.";
        foreach (var run in runs)
        {
            if (string.IsNullOrEmpty(run.Text) || run.Text.Any(char.IsControl))
                return "Name parts cannot be empty or contain control characters.";
            if (run.Color is not null && !HexColour().IsMatch(run.Color) || run.Background is not null && !HexColour().IsMatch(run.Background))
                return "Name colours must look like #10B981.";
            if (run.Font is { Length: > 0 } font && !Fonts.Contains(font))
                return "Choose one of the offered fonts.";
            if (run.FontSize is < 8 or > 72)
                return "Font sizes run from 8 to 72.";
        }
        return null;
    }

    private static string? Problem(SaveBrandingRequest r)
    {
        if (RunsProblem(r.NameStyle, 80) is { } runsProblem)
            return runsProblem;
        foreach (var (url, host) in new[] { (r.FacebookUrl, "facebook.com"), (r.InstagramUrl, "instagram.com") })
        {
            if (Clean(url) is not { } value)
                continue;
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
                || !(uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase)))
                return $"Use an https://{host}/… link.";
        }
        if (Clean(r.Phone) is { } phone && !PhonePattern().IsMatch(phone))
            return "Use digits, spaces, + and - in the phone number.";
        foreach (var text in new[] { r.DisplayName, r.Tagline, r.Address })
            if (text is not null && text.Any(char.IsControl))
                return "Text fields cannot contain control characters.";
        return null;
    }

    internal static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static (string ContentType, string Extension)? ImageType(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 24 && data[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            return ("image/png", ".png");
        if (data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            return ("image/jpeg", ".jpg");
        if (data.Length >= 16 && data[..4].SequenceEqual("RIFF"u8) && data.Slice(8, 4).SequenceEqual("WEBP"u8))
            return ("image/webp", ".webp");
        return null;
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    internal static partial Regex HexColour();

    [GeneratedRegex(@"^[0-9+\- ()]{6,30}$")]
    private static partial Regex PhonePattern();
}
