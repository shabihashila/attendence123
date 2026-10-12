using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FegHR.SharedKernel.Tenancy;

/// <summary>
/// Binds the tenant session context on every EF connection open, so EF queries
/// and any stored procedure run on the same connection are filtered by RLS.
/// </summary>
public sealed class TenantConnectionInterceptor(ITenantContext tenant) : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        => TenantSessionBinder.Bind(connection, tenant);

    public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
        => TenantSessionBinder.BindAsync(connection, tenant, cancellationToken);
}
