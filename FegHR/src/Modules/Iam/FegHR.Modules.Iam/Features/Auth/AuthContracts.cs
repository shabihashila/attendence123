using System.ComponentModel.DataAnnotations;

namespace FegHR.Modules.Iam.Features.Auth;

internal sealed record LoginRequest(
    [property: Required, StringLength(254)] string Login,
    [property: Required, StringLength(128)] string Password);

internal sealed record SelectTenantRequest(
    [property: Required, StringLength(4000)] string SelectionToken,
    [property: Range(1, int.MaxValue)] int TenantId);

/// <summary>
/// Moves a signed-in user to another company they belong to. The presented refresh
/// token, when given, is revoked so the previous company's session ends with the switch.
/// </summary>
internal sealed record SwitchTenantRequest(
    [property: Range(1, int.MaxValue)] int TenantId,
    [property: StringLength(200)] string? RefreshToken);

internal sealed record DefaultCompanyRequest([property: Range(1, int.MaxValue)] int TenantId);

internal sealed record RefreshRequest([property: Required, StringLength(200)] string RefreshToken);

internal sealed record TenantOption(int TenantId, string Slug, string Name, int? ParentTenantRefId, string? ParentName, bool HasChildConcerns, bool IsDefault = false);

internal sealed record AuthSession(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    TenantOption Tenant);

/// <summary>
/// Normal sign-in opens the user's saved accessible default in <see cref="Session"/>.
/// Selection-ticket fields remain available for clients completing an older selection flow.
/// </summary>
internal sealed record LoginResponse(
    bool RequiresTenantSelection,
    AuthSession? Session,
    string? SelectionToken,
    IReadOnlyList<TenantOption>? Tenants);

/// <summary>Outcome of an auth operation: data on success, otherwise an HTTP status and a message.</summary>
internal sealed record AuthOutcome<T>(T? Data, int Status, string? Error)
{
    public bool Succeeded => Error is null;

    public static AuthOutcome<T> Ok(T data) => new(data, 200, null);

    public static AuthOutcome<T> Fail(int status, string error) => new(default, status, error);
}
