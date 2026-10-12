using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace FegHR.SharedKernel.Tenancy;

/// <summary>
/// Opens Dapper connections for stored-procedure calls. The connection is
/// returned open and already bound to the caller's tenant.
/// </summary>
public interface ITenantDbConnectionFactory
{
    Task<DbConnection> OpenAsync(CancellationToken ct = default);
}

internal sealed class TenantDbConnectionFactory(IConfiguration configuration, ITenantContext tenant)
    : ITenantDbConnectionFactory
{
    private readonly string _connectionString = configuration.GetConnectionString(DatabaseNames.ConnectionStringName)
        ?? throw new InvalidOperationException(
            $"Connection string '{DatabaseNames.ConnectionStringName}' is not configured.");

    public async Task<DbConnection> OpenAsync(CancellationToken ct = default)
    {
        var connection = new SqlConnection(_connectionString);
        try
        {
            await connection.OpenAsync(ct);
            await TenantSessionBinder.BindAsync(connection, tenant, ct);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}

public static class DatabaseNames
{
    public const string ConnectionStringName = "DefaultConnection";
}
