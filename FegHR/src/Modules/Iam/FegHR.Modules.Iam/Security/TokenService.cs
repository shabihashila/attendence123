using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FegHR.SharedKernel.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Shared.Time;

namespace FegHR.Modules.Iam.Security;

/// <summary>
/// Issues staff access tokens, short-lived tenant-selection tickets and opaque
/// refresh tokens.
/// <list type="bullet">
/// <item>Access token: JWT, audience <see cref="Audiences.Staff"/>, claims <c>sub</c> (global user id),
/// <c>tid</c>, <c>tuid</c> (membership id), <c>name</c>, <c>pa</c>.</item>
/// <item>Selection ticket: JWT with its own audience, so the bearer scheme never accepts it.</item>
/// <item>Refresh token: <c>{tenantId}.{random}</c>. The tenant prefix lets refresh bind RLS before
/// lookup; only the SHA-256 of the whole string is stored.</item>
/// </list>
/// </summary>
internal sealed class TokenService(IOptions<JwtSettings> options, IAppTimeProvider clock)
{
    public const string SelectionAudience = "feghr-tenant-selection";
    public const string TenantUserIdClaim = "tuid";

    private static readonly TimeSpan SelectionLifetime = TimeSpan.FromMinutes(5);
    private readonly JwtSettings _settings = options.Value;
    private readonly JsonWebTokenHandler _handler = new() { SetDefaultTimesOnTokenCreation = false };

    public IssuedAccessToken CreateAccessToken(int userId, int tenantId, int tenantUserId, string displayName, bool isPlatformAdmin)
    {
        var now = clock.UtcNowOffset();
        var expires = now.AddMinutes(_settings.AccessTokenMinutes);
        var claims = new Dictionary<string, object>
        {
            [ClaimNames.Subject] = userId.ToString(),
            [ClaimNames.TenantId] = tenantId.ToString(),
            [TenantUserIdClaim] = tenantUserId.ToString(),
            [ClaimNames.Name] = displayName,
            [ClaimNames.TokenId] = Guid.NewGuid().ToString("N")
        };
        if (isPlatformAdmin)
            claims[ClaimNames.PlatformAdmin] = "1";

        return new IssuedAccessToken(Create(Audiences.Staff, claims, now, expires), expires);
    }

    public string CreateSelectionTicket(int userId)
    {
        var now = clock.UtcNowOffset();
        return Create(SelectionAudience, new Dictionary<string, object> { [ClaimNames.Subject] = userId.ToString() },
            now, now.Add(SelectionLifetime));
    }

    /// <summary>The user id inside a valid, unexpired selection ticket, or null.</summary>
    public async Task<int?> ReadSelectionTicketAsync(string ticket)
    {
        var result = await _handler.ValidateTokenAsync(ticket, new TokenValidationParameters
        {
            ValidIssuer = _settings.Issuer,
            ValidAudience = SelectionAudience,
            IssuerSigningKey = SigningKey(),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            LifetimeValidator = (notBefore, expires, _, _) => expires is { } e && e > clock.UtcNow()
        });
        if (!result.IsValid || result.ClaimsIdentity.FindFirst(ClaimNames.Subject)?.Value is not { } sub)
            return null;
        return int.TryParse(sub, out var userId) && userId > 0 ? userId : null;
    }

    public IssuedRefreshToken CreateRefreshToken(int tenantId)
    {
        var token = $"{tenantId}.{Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32))}";
        return new IssuedRefreshToken(token, HashRefreshToken(token), clock.UtcNowOffset().AddDays(_settings.RefreshTokenDays));
    }

    public static byte[] HashRefreshToken(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    /// <summary>The tenant prefix of a refresh token, without trusting anything else in it.</summary>
    public static int? ReadRefreshTokenTenant(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 200)
            return null;
        var dot = token.IndexOf('.');
        return dot > 0 && int.TryParse(token.AsSpan(0, dot), out var tenantId) && tenantId > 0 ? tenantId : null;
    }

    private string Create(string audience, Dictionary<string, object> claims, DateTimeOffset now, DateTimeOffset expires)
        => _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _settings.Issuer,
            Audience = audience,
            Claims = claims,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(SigningKey(), SecurityAlgorithms.HmacSha256)
        });

    private SymmetricSecurityKey SigningKey() => new(Encoding.UTF8.GetBytes(_settings.SigningKey));
}

internal sealed record IssuedAccessToken(string Token, DateTimeOffset ExpiresAt);

internal sealed record IssuedRefreshToken(string Token, byte[] Hash, DateTimeOffset ExpiresAt);

internal static class ClaimsPrincipalExtensions
{
    public static int? TenantUserId(this ClaimsPrincipal user)
        => int.TryParse(user.FindFirst(TokenService.TenantUserIdClaim)?.Value, out var id) && id > 0 ? id : null;
}
