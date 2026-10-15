using FegHR.Modules.Iam.Domain;
using FegHR.Modules.Iam.Persistence;
using FegHR.Modules.Iam.Security;
using FegHR.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Shared.Time;

namespace FegHR.Modules.Iam.Features.Auth;

/// <summary>
/// Sign-in, company selection, refresh-token rotation and sign-out.
/// Sign-in requests are anonymous, so every tenant-scoped write here runs in
/// an explicit tenant scope (<see cref="TenantScopes"/>) and stays under RLS.
/// </summary>
internal sealed class AuthService(
    IamProcedures procedures,
    TokenService tokens,
    IServiceScopeFactory scopes,
    IAppTimeProvider clock)
{
    private const string InvalidCredentials = "The sign-in details are not correct.";
    private const string SessionExpired = "Your session has expired. Please sign in again.";
    private static readonly ILogger Logger = Log.ForContext("SourceContext", typeof(AuthService).FullName);

    public async Task<AuthOutcome<LoginResponse>> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var normalized = LoginNormalizer.Normalize(request.Login);
        var user = normalized is null ? null : await procedures.GetLoginAsync(normalized, ct);
        if (user is null)
        {
            PasswordHasher.Verify(request.Password, null);
            return AuthOutcome<LoginResponse>.Fail(StatusCodes.Status401Unauthorized, InvalidCredentials);
        }

        var now = clock.UtcNowOffset();
        if (user.LockoutEnd is { } lockedUntil && lockedUntil > now)
            return AuthOutcome<LoginResponse>.Fail(StatusCodes.Status423Locked,
                "Too many failed attempts. The account is locked for a few minutes.");

        var passwordOk = PasswordHasher.Verify(request.Password, user.PasswordHash);
        await procedures.RecordLoginResultAsync(user.Id, passwordOk, now, ct);
        if (!passwordOk)
        {
            Logger.Information("Failed sign-in for user {UserId}", user.Id);
            return AuthOutcome<LoginResponse>.Fail(StatusCodes.Status401Unauthorized, InvalidCredentials);
        }

        var memberships = await procedures.GetTenantsAsync(user.Id, ct);
        if (memberships.Count == 0)
            return AuthOutcome<LoginResponse>.Fail(StatusCodes.Status403Forbidden,
                "Your account is not linked to an active company.");

        var defaultId = await procedures.ResolveDefaultCompanyAsync(user.Id, ct);
        var membership = memberships.FirstOrDefault(m => m.TenantId == defaultId);
        if (membership is null)
            return AuthOutcome<LoginResponse>.Fail(StatusCodes.Status403Forbidden, "Your company access changed. Please sign in again.");
        var session = await IssueSessionAsync(user.Id, user.DisplayName, user.IsPlatformAdmin, membership, ct);
        return AuthOutcome<LoginResponse>.Ok(new LoginResponse(false, session, null, null));
    }

    public async Task<AuthOutcome<AuthSession>> SelectTenantAsync(SelectTenantRequest request, CancellationToken ct)
    {
        if (await tokens.ReadSelectionTicketAsync(request.SelectionToken) is not { } userId)
            return AuthOutcome<AuthSession>.Fail(StatusCodes.Status401Unauthorized, SessionExpired);

        var user = await procedures.GetUserAsync(userId, ct);
        var membership = (await procedures.GetTenantsAsync(userId, ct)).FirstOrDefault(m => m.TenantId == request.TenantId);
        if (user is not { IsActive: true } || membership is null)
            return AuthOutcome<AuthSession>.Fail(StatusCodes.Status403Forbidden, "You do not have access to this company.");

        return AuthOutcome<AuthSession>.Ok(await IssueSessionAsync(user.Id, user.DisplayName, user.IsPlatformAdmin, membership, ct));
    }

    /// <summary>The companies the signed-in user can switch to, for the top-bar switcher.</summary>
    public async Task<IReadOnlyList<TenantOption>> GetTenantOptionsAsync(int userId, CancellationToken ct)
    {
        var memberships = await procedures.GetTenantsAsync(userId, ct);
        var defaultId = await procedures.ResolveDefaultCompanyAsync(userId, ct);
        return memberships.Select(m => ToOption(m) with { IsDefault = m.TenantId == defaultId }).OrderBy(t => t.Name).ToList();
    }

    /// <summary>
    /// Issues a session in another company without a new sign-in, the way DailyCommerce's
    /// company switcher works. Membership is checked again here, so a revoked membership or an
    /// inactive account cannot switch. A product owner reaches every company through the
    /// administrator memberships that registration and deployment maintain
    /// (<c>iam.usp_PlatformOwner_JoinTenant</c>).
    /// </summary>
    public async Task<AuthOutcome<AuthSession>> SwitchTenantAsync(int userId, SwitchTenantRequest request, CancellationToken ct)
    {
        var user = await procedures.GetUserAsync(userId, ct);
        var membership = user is { IsActive: true }
            ? (await procedures.GetTenantsAsync(userId, ct)).FirstOrDefault(m => m.TenantId == request.TenantId)
            : null;
        if (user is null || membership is null)
            return AuthOutcome<AuthSession>.Fail(StatusCodes.Status403Forbidden, "You do not have access to this company.");

        if (!string.IsNullOrWhiteSpace(request.RefreshToken))
            await LogoutAsync(new RefreshRequest(request.RefreshToken), ct);
        Logger.Information("User {UserId} switched to tenant {TenantId}", userId, membership.TenantId);
        return AuthOutcome<AuthSession>.Ok(await IssueSessionAsync(user.Id, user.DisplayName, user.IsPlatformAdmin, membership, ct));
    }

    /// <summary>
    /// Rotates a refresh token. Presenting a token that was already rotated
    /// means it leaked or was replayed: every active token of that membership
    /// is revoked, and the user must sign in again.
    /// </summary>
    public async Task<AuthOutcome<AuthSession>> RefreshAsync(RefreshRequest request, CancellationToken ct)
    {
        if (TokenService.ReadRefreshTokenTenant(request.RefreshToken) is not { } tenantId)
            return AuthOutcome<AuthSession>.Fail(StatusCodes.Status401Unauthorized, SessionExpired);

        await using var scope = scopes.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<IamDbContext>();
        var scopedProcedures = scope.ServiceProvider.GetRequiredService<IamProcedures>();
        var now = clock.UtcNowOffset();
        var hash = TokenService.HashRefreshToken(request.RefreshToken);

        var current = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (current is null)
            return AuthOutcome<AuthSession>.Fail(StatusCodes.Status401Unauthorized, SessionExpired);

        if (current.RevokedAt is not null)
        {
            await RevokeAllAsync(db, current.TenantUserId, now, ct);
            Logger.Warning("Refresh token reuse detected for tenant user {TenantUserId} in tenant {TenantId}",
                current.TenantUserId, tenantId);
            return AuthOutcome<AuthSession>.Fail(StatusCodes.Status401Unauthorized, SessionExpired);
        }
        if (current.ExpiresAt <= now)
            return AuthOutcome<AuthSession>.Fail(StatusCodes.Status401Unauthorized, SessionExpired);

        var member = await db.TenantUsers.AsNoTracking().SingleOrDefaultAsync(tu => tu.Id == current.TenantUserId, ct);
        var user = member is { IsActive: true } ? await scopedProcedures.GetUserAsync(member.UserId, ct) : null;
        var membership = user is { IsActive: true }
            ? (await scopedProcedures.GetTenantsAsync(user.Id, ct)).FirstOrDefault(m => m.TenantId == tenantId)
            : null;
        if (user is null || membership is null)
        {
            await RevokeAllAsync(db, current.TenantUserId, now, ct);
            return AuthOutcome<AuthSession>.Fail(StatusCodes.Status401Unauthorized, SessionExpired);
        }

        var next = tokens.CreateRefreshToken(tenantId);
        current.RevokedAt = now;
        current.ReplacedByTokenHash = next.Hash;
        db.RefreshTokens.Add(new RefreshToken { TenantUserId = current.TenantUserId, TokenHash = next.Hash, ExpiresAt = next.ExpiresAt });
        await db.SaveChangesAsync(ct);

        var access = tokens.CreateAccessToken(user.Id, tenantId, membership.TenantUserId, user.DisplayName, user.IsPlatformAdmin);
        return AuthOutcome<AuthSession>.Ok(new AuthSession(access.Token, access.ExpiresAt, next.Token, next.ExpiresAt, ToOption(membership)));
    }

    /// <summary>Revokes the presented refresh token. Always succeeds, so it reveals nothing.</summary>
    public async Task LogoutAsync(RefreshRequest request, CancellationToken ct)
    {
        if (TokenService.ReadRefreshTokenTenant(request.RefreshToken) is not { } tenantId)
            return;
        await using var scope = scopes.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<IamDbContext>();
        var hash = TokenService.HashRefreshToken(request.RefreshToken);
        var token = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash && t.RevokedAt == null, ct);
        if (token is null)
            return;
        token.RevokedAt = clock.UtcNowOffset();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Revokes every active refresh token of a membership (deactivation, reuse detection).</summary>
    public static async Task RevokeAllAsync(IamDbContext db, int tenantUserId, DateTimeOffset now, CancellationToken ct)
    {
        var active = await db.RefreshTokens.Where(t => t.TenantUserId == tenantUserId && t.RevokedAt == null).ToListAsync(ct);
        foreach (var token in active)
            token.RevokedAt = now;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Issues a session for a membership the caller has already verified.</summary>
    public async Task<AuthSession> IssueSessionAsync(int userId, string displayName, bool isPlatformAdmin,
        MembershipRecord membership, CancellationToken ct)
    {
        await using var scope = scopes.CreateTenantScope(membership.TenantId, userId);
        var db = scope.ServiceProvider.GetRequiredService<IamDbContext>();
        var refresh = tokens.CreateRefreshToken(membership.TenantId);
        db.RefreshTokens.Add(new RefreshToken
        {
            TenantUserId = membership.TenantUserId,
            TokenHash = refresh.Hash,
            ExpiresAt = refresh.ExpiresAt
        });
        await db.SaveChangesAsync(ct);

        var access = tokens.CreateAccessToken(userId, membership.TenantId, membership.TenantUserId, displayName, isPlatformAdmin);
        return new AuthSession(access.Token, access.ExpiresAt, refresh.Token, refresh.ExpiresAt, ToOption(membership));
    }

    private static TenantOption ToOption(MembershipRecord m) => new(m.TenantId, m.Slug, m.Name, m.ParentTenantRefId, m.ParentName, m.HasChildConcerns);
}
