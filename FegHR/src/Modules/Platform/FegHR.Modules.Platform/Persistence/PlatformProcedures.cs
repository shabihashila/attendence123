using System.Data;
using Dapper;
using FegHR.Modules.Platform.Contracts;
using FegHR.SharedKernel.Tenancy;

namespace FegHR.Modules.Platform.Persistence;

/// <summary>
/// Stored procedures of the <c>platform</c> schema. The company, host and licence tables are
/// denied to the application role, so they are read and written only through these.
/// </summary>
internal sealed class PlatformProcedures(ITenantDbConnectionFactory connections)
{
    /// <summary>Unbound lookup: matches the slug or a registered host name.</summary>
    public async Task<int?> ResolveTenantAsync(string hostOrSlug, CancellationToken ct)
    {
        var row = await FirstAsync<ResolvedTenant>("platform.usp_Tenant_ResolveByHost", new { Host = hostOrSlug }, ct);
        return row?.TenantId;
    }

    /// <summary>The bound company with its settings.</summary>
    public Task<TenantSummary?> GetCurrentTenantAsync(CancellationToken ct)
        => FirstAsync<TenantSummary>("platform.usp_Tenant_GetCurrent", null, ct);

    public async Task<IReadOnlyList<string>> GetFeatureCodesAsync(DateTimeOffset now, CancellationToken ct)
        => await ListAsync<string>("platform.usp_Tenant_GetFeatureCodes", new { Now = now }, ct);

    public Task<LicenseStatus?> GetLicenseStatusAsync(DateTimeOffset now, CancellationToken ct)
        => FirstAsync<LicenseStatus>("platform.usp_License_GetStatus", new { Now = now }, ct);

    /// <summary>Public: whether registration is open and what trial it grants.</summary>
    public Task<RegistrationOffer?> GetRegistrationOfferAsync(CancellationToken ct)
        => FirstAsync<RegistrationOffer>("platform.usp_Registration_GetOffer", null, ct);

    // ---- product owner console (the procedures require the platform-administrator session flag) ----

    public Task<IReadOnlyList<OwnerTenantRow>> OwnerListTenantsAsync(DateTimeOffset now, string? search, int skip, int take,
        CancellationToken ct)
        => ListAsync<OwnerTenantRow>("platform.usp_Owner_Tenant_List", new { Now = now, Search = search, Skip = skip, Take = take }, ct);

    public Task OwnerSetTenantStatusAsync(int tenantId, string status, DateTimeOffset now, int actorId, CancellationToken ct)
        => ExecuteAsync("platform.usp_Owner_Tenant_SetStatus", new { TenantId = tenantId, Status = status, Now = now, ActorId = actorId }, ct);

    public Task<IReadOnlyList<OwnerLicenseRow>> OwnerListLicensesAsync(int tenantId, CancellationToken ct)
        => ListAsync<OwnerLicenseRow>("platform.usp_Owner_License_List", new { TenantId = tenantId }, ct);

    public Task OwnerIssueLicenseAsync(int tenantId, string planCode, string kind, DateTimeOffset startsAt, DateTimeOffset? endsAt,
        string? licenseKey, string? note, DateTimeOffset now, int actorId, CancellationToken ct)
        => ExecuteAsync("platform.usp_Owner_License_Issue", new
        {
            TenantId = tenantId, PlanCode = planCode, Kind = kind, StartsAt = startsAt, EndsAt = endsAt,
            LicenseKey = licenseKey, Note = note, Now = now, ActorId = actorId
        }, ct);

    public Task OwnerRevokeLicenseAsync(int licenseId, DateTimeOffset now, int actorId, CancellationToken ct)
        => ExecuteAsync("platform.usp_Owner_License_Revoke", new { LicenseId = licenseId, Now = now, ActorId = actorId }, ct);

    public Task<LicensePolicyRow?> OwnerGetPolicyAsync(CancellationToken ct)
        => FirstAsync<LicensePolicyRow>("platform.usp_Owner_Policy_Get", null, ct);

    public Task OwnerSetPolicyAsync(int trialDays, string trialPlanCode, bool registrationOpen, byte[] rowVersion,
        DateTimeOffset now, int actorId, CancellationToken ct)
    {
        var p = new DynamicParameters(new { TrialDays = trialDays, TrialPlanCode = trialPlanCode, RegistrationOpen = registrationOpen, Now = now, ActorId = actorId });
        p.Add("RowVersion", rowVersion, DbType.Binary, size: 8);
        return ExecuteAsync("platform.usp_Owner_Policy_Set", p, ct);
    }

    private async Task<T?> FirstAsync<T>(string procedure, object? parameters, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        return await connection.QueryFirstOrDefaultAsync<T>(new CommandDefinition(procedure, parameters,
            commandType: CommandType.StoredProcedure, cancellationToken: ct));
    }

    private async Task<IReadOnlyList<T>> ListAsync<T>(string procedure, object? parameters, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        return (await connection.QueryAsync<T>(new CommandDefinition(procedure, parameters,
            commandType: CommandType.StoredProcedure, cancellationToken: ct))).ToList();
    }

    private async Task ExecuteAsync(string procedure, object parameters, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(procedure, parameters,
            commandType: CommandType.StoredProcedure, cancellationToken: ct));
    }

    private sealed record ResolvedTenant(int TenantId, string Slug, string Name);
}

internal sealed record RegistrationOffer(bool RegistrationOpen, int TrialDays, string PlanName);

internal sealed class OwnerTenantRow
{
    public int Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Origin { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public bool IsLicensed { get; set; }
    public bool IsOwnerCompany { get; set; }
    public string? LicenseKind { get; set; }
    public string? PlanName { get; set; }
    public DateTimeOffset? LicenseEndsAt { get; set; }
    public int TotalCount { get; set; }
}

internal sealed class OwnerLicenseRow
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public string PlanCode { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public string? LicenseKey { get; set; }
    public string? Note { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class LicensePolicyRow
{
    public int TrialDays { get; set; }
    public string TrialPlanCode { get; set; } = string.Empty;
    public bool RegistrationOpen { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
