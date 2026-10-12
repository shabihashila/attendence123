namespace FegHR.SharedKernel.Persistence;

/// <summary>A row in a tenant table (ARCHITECTURE.md section 4). Stamped on insert from the tenant context.</summary>
public interface ITenantOwned
{
    int TenantId { get; set; }
}

/// <summary>The standard audit columns, stamped by <see cref="AuditStampInterceptor"/>.</summary>
public interface IAuditable
{
    DateTimeOffset CreatedAt { get; set; }
    int? CreatedBy { get; set; }
    DateTimeOffset? UpdatedAt { get; set; }
    int? UpdatedBy { get; set; }
}

/// <summary>Optimistic concurrency through the <c>RowVersion ROWVERSION</c> column.</summary>
public interface IVersioned
{
    byte[] RowVersion { get; set; }
}

/// <summary>
/// Convenience base for the common case: identity key, tenant, audit columns and row version.
/// Entities are free to implement the interfaces directly instead.
/// </summary>
public abstract class TenantEntity : ITenantOwned, IAuditable, IVersioned
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public int? UpdatedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
