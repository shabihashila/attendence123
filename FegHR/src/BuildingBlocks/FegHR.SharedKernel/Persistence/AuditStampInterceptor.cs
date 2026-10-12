using FegHR.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shared.Time;

namespace FegHR.SharedKernel.Persistence;

/// <summary>
/// Stamps tenant and audit columns before EF saves. The tenant comes from the
/// request context, never from the client: a row whose <c>TenantId</c> differs
/// from the bound tenant is rejected here, and RLS block predicates reject it
/// again in SQL.
/// </summary>
public sealed class AuditStampInterceptor(ITenantContext tenant, IAppTimeProvider clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
            return;

        var now = clock.UtcNowOffset();
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is ITenantOwned owned)
            {
                if (entry.State == EntityState.Added)
                {
                    var tenantId = tenant.RequiredTenantId;
                    if (owned.TenantId != 0 && owned.TenantId != tenantId)
                        throw new InvalidOperationException("Cannot insert a row for a different tenant.");
                    owned.TenantId = tenantId;
                }
                else if (entry.State == EntityState.Modified)
                {
                    entry.Property(nameof(ITenantOwned.TenantId)).IsModified = false;
                }
            }

            if (entry.Entity is IAuditable audited)
            {
                if (entry.State == EntityState.Added)
                {
                    audited.CreatedAt = now;
                    audited.CreatedBy = tenant.UserId;
                }
                else if (entry.State == EntityState.Modified)
                {
                    audited.UpdatedAt = now;
                    audited.UpdatedBy = tenant.UserId;
                    entry.Property(nameof(IAuditable.CreatedAt)).IsModified = false;
                    entry.Property(nameof(IAuditable.CreatedBy)).IsModified = false;
                }
            }
        }
    }
}
