using System.Data;
using System.Data.Common;
using Dapper;

namespace FegHR.SharedKernel.Tenancy;

/// <summary>
/// Publishes the tenant context into SQL Server <c>SESSION_CONTEXT</c>. The
/// row-level security predicate <c>security.fn_TenantAccess</c> and the
/// <c>TenantId</c> default constraints read these keys.
/// </summary>
/// <remarks>
/// Every key is written on every connection open, including null values: a
/// pooled connection must never carry the previous checkout's tenant.
/// </remarks>
public static class TenantSessionBinder
{
    public const string TenantIdKey = "FegHR.TenantId";
    public const string UserIdKey = "FegHR.UserId";
    public const string IsPlatformAdminKey = "FegHR.IsPlatformAdmin";

    public static void Bind(IDbConnection connection, ITenantContext tenant)
    {
        foreach (var (key, value) in Values(tenant))
            connection.Execute("sys.sp_set_session_context", new { key, value },
                commandType: CommandType.StoredProcedure);
    }

    public static async Task BindAsync(DbConnection connection, ITenantContext tenant, CancellationToken ct = default)
    {
        foreach (var (key, value) in Values(tenant))
            await connection.ExecuteAsync(new CommandDefinition("sys.sp_set_session_context",
                new { key, value }, commandType: CommandType.StoredProcedure, cancellationToken: ct));
    }

    private static IEnumerable<(string Key, object? Value)> Values(ITenantContext tenant)
    {
        yield return (TenantIdKey, tenant.TenantId);
        yield return (UserIdKey, tenant.UserId);
        yield return (IsPlatformAdminKey, tenant.IsPlatformAdmin ? 1 : 0);
    }
}
