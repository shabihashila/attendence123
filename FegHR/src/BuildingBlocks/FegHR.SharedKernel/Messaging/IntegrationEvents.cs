using System.Collections.Concurrent;
using System.Reflection;

namespace FegHR.SharedKernel.Messaging;

/// <summary>
/// A fact one module publishes for others, e.g. <c>OrderSettled</c>. Declared in
/// the publisher's <c>.Contracts</c> project as an immutable record carrying
/// <see cref="IntegrationEventAttribute"/>.
/// </summary>
public interface IIntegrationEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredAt { get; }
}

/// <summary>
/// The stable wire name of an event, e.g. <c>sales.order-settled.v1</c>. The
/// outbox stores this name, not a .NET type name, so stored procedures can
/// publish events and contract types can be renamed safely. Change the
/// version suffix when the payload shape changes incompatibly.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class IntegrationEventAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>Name ↔ type map for events this process publishes or consumes.</summary>
public static class IntegrationEventNames
{
    private static readonly ConcurrentDictionary<string, Type> ByName = new(StringComparer.Ordinal);

    public static string Of(Type eventType)
    {
        var name = eventType.GetCustomAttribute<IntegrationEventAttribute>()?.Name;
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException(
                $"{eventType.FullName} needs [IntegrationEvent(\"<module>.<event>.v1\")] to be published or consumed.");
        var existing = ByName.GetOrAdd(name, eventType);
        if (existing != eventType)
            throw new InvalidOperationException($"Event name '{name}' is used by both {existing.FullName} and {eventType.FullName}.");
        return name;
    }

    /// <summary>The contract type for a stored name, when a handler in this process registered it.</summary>
    public static Type? Resolve(string name) => ByName.TryGetValue(name, out var type) ? type : null;
}

/// <summary>
/// Handles one event type in a consuming module. Runs in a background scope
/// bound to the event's tenant. Implementations must be idempotent; derive
/// from <see cref="InboxEventHandler{TEvent, TContext}"/> to get that for free.
/// </summary>
public interface IIntegrationEventHandler<in TEvent> where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken ct);
}

/// <summary>
/// Queues events in the publisher's outbox. The row is written by the
/// publisher's own <c>SaveChanges</c>, so the event commits or rolls back with
/// the business change that raised it.
/// </summary>
public interface IOutbox
{
    void Enqueue<TContext>(TContext context, IIntegrationEvent integrationEvent)
        where TContext : Persistence.ModuleDbContext;
}
