using System.Text.Json;
using FegHR.SharedKernel.Http;
using FegHR.SharedKernel.Persistence;
using FegHR.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shared.Time;

namespace FegHR.SharedKernel.Messaging;

/// <summary>
/// <c>&lt;schema&gt;.Outbox</c>. Not under row-level security: the dispatcher
/// reads every tenant's pending rows, then handles each one in a scope bound
/// to that row's tenant.
/// <code>
/// Id BIGINT IDENTITY PK, TenantId INT NOT NULL, EventId UNIQUEIDENTIFIER NOT NULL UNIQUE,
/// EventType NVARCHAR(400) NOT NULL, Payload NVARCHAR(MAX) NOT NULL,
/// OccurredAt DATETIMEOFFSET(3) NOT NULL, ProcessedAt DATETIMEOFFSET(3) NULL,
/// Attempts INT NOT NULL DEFAULT 0, LastError NVARCHAR(2000) NULL
/// </code>
/// </summary>
public sealed class OutboxMessage
{
    public long Id { get; set; }
    public int TenantId { get; set; }
    public Guid EventId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }

    internal static void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.ToTable("Outbox");
        b.HasKey(x => x.Id);
        b.Property(x => x.EventType).HasMaxLength(400);
        b.Property(x => x.LastError).HasMaxLength(2000);
    }
}

/// <summary>
/// <c>&lt;schema&gt;.Inbox</c>: one row per event a handler has applied.
/// <code>
/// EventId UNIQUEIDENTIFIER, Handler NVARCHAR(300), TenantId INT NOT NULL,
/// ProcessedAt DATETIMEOFFSET(3) NOT NULL, PK (EventId, Handler)
/// </code>
/// </summary>
public sealed class InboxMessage
{
    public Guid EventId { get; set; }
    public string Handler { get; set; } = string.Empty;
    public int TenantId { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }

    internal static void Configure(EntityTypeBuilder<InboxMessage> b)
    {
        b.ToTable("Inbox");
        b.HasKey(x => new { x.EventId, x.Handler });
        b.Property(x => x.Handler).HasMaxLength(300);
    }
}

internal sealed class Outbox(ITenantContext tenant) : IOutbox
{
    public void Enqueue<TContext>(TContext context, IIntegrationEvent integrationEvent)
        where TContext : ModuleDbContext
    {
        var type = integrationEvent.GetType();
        context.Set<OutboxMessage>().Add(new OutboxMessage
        {
            TenantId = tenant.RequiredTenantId,
            EventId = integrationEvent.EventId,
            EventType = IntegrationEventNames.Of(type),
            Payload = JsonSerializer.Serialize(integrationEvent, type, ApiJson.Options),
            OccurredAt = integrationEvent.OccurredAt
        });
    }
}

/// <summary>
/// Idempotent handler base: applies the event and records it in the
/// consumer's inbox in one transaction, so a redelivery is a no-op.
/// </summary>
public abstract class InboxEventHandler<TEvent, TContext>(TContext db, IAppTimeProvider clock, ITenantContext tenant)
    : IIntegrationEventHandler<TEvent>
    where TEvent : IIntegrationEvent
    where TContext : ModuleDbContext
{
    protected TContext Db { get; } = db;

    public async Task HandleAsync(TEvent integrationEvent, CancellationToken ct)
    {
        var handler = GetType().FullName!;
        if (await Db.Set<InboxMessage>().AnyAsync(
                m => m.EventId == integrationEvent.EventId && m.Handler == handler, ct))
            return;

        await using var transaction = await Db.Database.BeginTransactionAsync(ct);
        await ApplyAsync(integrationEvent, ct);
        Db.Set<InboxMessage>().Add(new InboxMessage
        {
            EventId = integrationEvent.EventId,
            Handler = handler,
            TenantId = tenant.RequiredTenantId,
            ProcessedAt = clock.UtcNowOffset()
        });
        await Db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    /// <summary>The module's reaction. Stage changes on <see cref="Db"/>; the base saves and commits.</summary>
    protected abstract Task ApplyAsync(TEvent integrationEvent, CancellationToken ct);
}
