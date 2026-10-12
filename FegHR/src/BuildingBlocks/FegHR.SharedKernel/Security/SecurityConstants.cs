namespace FegHR.SharedKernel.Security;

/// <summary>JWT claim names. Inbound claim mapping is off, so these arrive verbatim.</summary>
public static class ClaimNames
{
    public const string Subject = "sub";
    public const string TenantId = "tid";
    public const string PlatformAdmin = "pa";
    public const string Name = "name";
    public const string Audience = "aud";
    public const string TokenId = "jti";
}

/// <summary>Token audiences. Access tokens carry <see cref="Staff"/>; other tickets use their own.</summary>
public static class Audiences
{
    public const string Staff = "feghr-staff";
}

public static class PolicyNames
{
    public const string Staff = "Staff";
    public const string PlatformAdmin = "PlatformAdmin";
}

/// <summary>Named rate-limit policies registered by the host.</summary>
public static class RateLimitPolicies
{
    /// <summary>Credential endpoints: 5 attempts per minute per client IP.</summary>
    public const string Login = "login";

    /// <summary>Public company registration: 5 attempts per 10 minutes per client IP.</summary>
    public const string Registration = "registration";
}

public sealed class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "feghr";

    /// <summary>HMAC-SHA256 key, at least 32 bytes. Kept in user-secrets or the server environment, never in git.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 30;
    public int RefreshTokenDays { get; set; } = 14;
}
