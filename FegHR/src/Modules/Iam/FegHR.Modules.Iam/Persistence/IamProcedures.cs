using System.Data;
using System.Text.Json;
using Dapper;
using FegHR.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FegHR.Modules.Iam.Persistence;

/// <summary>
/// The <c>iam</c> stored procedures. Global users are reachable only
/// through these: the application role is DENY on <c>iam.Users</c>.
/// <para>
/// Calls that must commit together with EF changes run on the DbContext's own
/// connection and transaction (<see cref="OnContext"/>); the rest open a
/// tenant-bound connection of their own.
/// </para>
/// </summary>
internal sealed class IamProcedures(ITenantDbConnectionFactory connections)
{
    public Task<LoginRecord?> GetLoginAsync(string normalizedLogin, CancellationToken ct)
        => QuerySingleAsync<LoginRecord>("iam.usp_User_GetLogin", new { NormalizedLogin = normalizedLogin }, ct);

    public Task<UserRecord?> GetUserAsync(int userId, CancellationToken ct)
        => QuerySingleAsync<UserRecord>("iam.usp_User_GetById", new { UserId = userId }, ct);

    public async Task RecordLoginResultAsync(int userId, bool succeeded, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("iam.usp_User_RecordLoginResult",
            new { UserId = userId, Succeeded = succeeded, Now = now, MaxFailures = 5, LockoutMinutes = 15 },
            commandType: CommandType.StoredProcedure, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<MembershipRecord>> GetTenantsAsync(int userId, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        var rows = await connection.QueryAsync<MembershipRecord>(new CommandDefinition("iam.usp_User_GetTenants",
            new { UserId = userId }, commandType: CommandType.StoredProcedure, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>Tenant-bound: the user's permission codes in the bound tenant.</summary>
    public async Task<IReadOnlyList<string>> GetPermissionsAsync(int userId, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        var rows = await connection.QueryAsync<string>(new CommandDefinition("iam.usp_User_GetPermissions",
            new { UserId = userId }, commandType: CommandType.StoredProcedure, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>Tenant-bound: flat menu rows visible to the user under the plan's feature codes.</summary>
    public async Task<IReadOnlyList<MenuRecord>> GetNavigationAsync(int userId, IEnumerable<string> featureCodes, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        var rows = await connection.QueryAsync<MenuRecord>(new CommandDefinition("iam.usp_User_GetNavigation",
            new { UserId = userId, FeatureCodesJson = JsonSerializer.Serialize(featureCodes) },
            commandType: CommandType.StoredProcedure, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>Tenant-bound: members of the bound tenant with their global profile.</summary>
    public async Task<IReadOnlyList<MemberRecord>> ListMembersAsync(CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        var rows = await connection.QueryAsync<MemberRecord>(new CommandDefinition("iam.usp_TenantUser_List",
            commandType: CommandType.StoredProcedure, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// Public registration: company, administrator account, trial licence and first employee in
    /// one transaction (<c>iam.usp_Company_Register</c>). Runs unbound; the procedure binds the
    /// new company for its own statements and restores the session afterwards.
    /// </summary>
    public async Task<RegisteredCompany> RegisterCompanyAsync(object parameters, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        return await connection.QuerySingleAsync<RegisteredCompany>(new CommandDefinition("iam.usp_Company_Register",
            parameters, commandType: CommandType.StoredProcedure, cancellationToken: ct));
    }

    public async Task<string?> GetPasswordHashAsync(int userId, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition("iam.usp_User_GetPasswordHash",
            new { UserId = userId }, commandType: CommandType.StoredProcedure, cancellationToken: ct));
    }

    public async Task SetPasswordAsync(int userId, string passwordHash, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("iam.usp_User_SetPassword",
            new { UserId = userId, PasswordHash = passwordHash, Now = now }, commandType: CommandType.StoredProcedure,
            cancellationToken: ct));
    }

    /// <summary>Procedures that must share the DbContext's connection and transaction.</summary>
    public static ContextProcedures OnContext(IamDbContext db) => new(db);

    private async Task<T?> QuerySingleAsync<T>(string procedure, object parameters, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        return await connection.QueryFirstOrDefaultAsync<T>(new CommandDefinition(procedure, parameters,
            commandType: CommandType.StoredProcedure, cancellationToken: ct));
    }

    internal sealed class ContextProcedures(IamDbContext db)
    {
        /// <summary>Creates a global user; the procedure normalizes and rejects duplicates (THROW 51030).</summary>
        public Task<int> CreateUserAsync(string? email, string? phone, string displayName, string passwordHash,
            DateTimeOffset now, CancellationToken ct)
            => ScalarAsync("iam.usp_User_Create",
                new { Email = email, Phone = phone, DisplayName = displayName, PasswordHash = passwordHash, Now = now }, ct);

        /// <summary>Adds (or reactivates) the user's membership of the bound tenant; returns the TenantUserId.</summary>
        public Task<int> AddMembershipAsync(int userId, DateTimeOffset now, CancellationToken ct)
            => ScalarAsync("iam.usp_TenantUser_Add", new { UserId = userId, Now = now }, ct);

        public async Task SetMembershipActiveAsync(int tenantUserId, bool isActive, DateTimeOffset now, CancellationToken ct)
        {
            var (connection, transaction) = await OpenAsync(ct);
            await connection.ExecuteAsync(new CommandDefinition("iam.usp_TenantUser_SetActive",
                new { TenantUserId = tenantUserId, IsActive = isActive, Now = now }, transaction,
                commandType: CommandType.StoredProcedure, cancellationToken: ct));
        }

        private async Task<int> ScalarAsync(string procedure, object parameters, CancellationToken ct)
        {
            var (connection, transaction) = await OpenAsync(ct);
            return await connection.ExecuteScalarAsync<int>(new CommandDefinition(procedure, parameters, transaction,
                commandType: CommandType.StoredProcedure, cancellationToken: ct));
        }

        // Opening through EF runs the tenant interceptor, so the session context is bound.
        private async Task<(IDbConnection, IDbTransaction?)> OpenAsync(CancellationToken ct)
        {
            await db.Database.OpenConnectionAsync(ct);
            return (db.Database.GetDbConnection(), db.Database.CurrentTransaction?.GetDbTransaction());
        }
    }
}

internal sealed record LoginRecord(int Id, string DisplayName, string? PasswordHash, Guid SecurityStamp,
    DateTimeOffset? LockoutEnd, int AccessFailedCount, bool IsPlatformAdmin);

internal sealed record UserRecord(int Id, string DisplayName, string? Email, string? Phone, bool IsActive, bool IsPlatformAdmin);

internal sealed record MembershipRecord(int TenantId, string Slug, string Name, int TenantUserId);

internal sealed record RegisteredCompany(int TenantId, string Slug, int UserId, int TenantUserId, DateTimeOffset TrialEndsAt);

internal sealed record MenuRecord(int Id, int? ParentId, string Code, string Title, string? Icon, string? Route,
    int SortOrder, string? FeatureCode);

internal sealed record MemberRecord(int TenantUserId, int UserId, string DisplayName, string? Email, string? Phone, bool IsActive,
    bool IsPlatformAdmin);
