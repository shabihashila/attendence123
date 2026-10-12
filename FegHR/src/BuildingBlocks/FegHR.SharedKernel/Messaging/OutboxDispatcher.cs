using System.Reflection;
using System.Text.Json;
using FegHR.SharedKernel.Http;
using FegHR.SharedKernel.Persistence;
using FegHR.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Shared.Time;

namespace FegHR.SharedKernel.Messaging;

/// <summary>
/// Delivers one publishing module's outbox to every registered handler,
/// at least once. Each message is handled in its own DI scope bound to the
/// message's tenant, so handlers run under the same RLS rules as a request.
/// </summary>
/// <remarks>
/// Designed for a single API instance (the IIS deployment). Running several
/// instances needs row claiming in a stored procedure (UPDLOCK, READPAST)
/// before scaling out.
/// </remarks>
internal sealed class OutboxDispatcher<TContext>(IServiceScopeFactory scopes) : BackgroundService
    where TContext : ModuleDbContext
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ErrorBackoff = TimeSpan.FromSeconds(30);
    private const int BatchSize = 50;
    private const int MaxAttempts = 10;

    private static readonly ILogger Logger = Log.ForContext("SourceContext", typeof(OutboxDispatcher<TContext>).FullName);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delivered = 0;
            try
            {
                delivered = await DispatchBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // Back off on infrastructure failures (database down, schema not deployed)
                // instead of logging every poll.
                Logger.Error(ex, "Outbox dispatch for {Context} failed; retrying in {Seconds}s", typeof(TContext).Name,
                    ErrorBackoff.TotalSeconds);
                await Task.Delay(ErrorBackoff, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                continue;
            }

            if (delivered < BatchSize)
                await Task.Delay(PollInterval, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    private async Task<int> DispatchBatchAsync(CancellationToken ct)
    {
        List<OutboxMessage> pending;
        using (var readScope = scopes.CreateScope())
        {
            readScope.ServiceProvider.GetRequiredService<TenantContext>()
                .Bind(null, null, null, false, TenantSource.BackgroundScope);
            var db = readScope.ServiceProvider.GetRequiredService<TContext>();
            pending = await db.Set<OutboxMessage>().AsNoTracking()
                .Where(m => m.ProcessedAt == null && m.Attempts < MaxAttempts)
                .OrderBy(m => m.Id)
                .Take(BatchSize)
                .ToListAsync(ct);
        }

        foreach (var message in pending)
            await DeliverAsync(message, ct);

        return pending.Count;
    }

    private async Task DeliverAsync(OutboxMessage message, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<TenantContext>()
            .Bind(message.TenantId, null, null, false, TenantSource.BackgroundScope);
        var db = services.GetRequiredService<TContext>();
        var clock = services.GetRequiredService<IAppTimeProvider>();
        var row = await db.Set<OutboxMessage>().SingleAsync(m => m.Id == message.Id, ct);

        try
        {
            // No handler in this process registered the name: nothing consumes it, so it is done.
            if (IntegrationEventNames.Resolve(message.EventType) is not { } eventType)
            {
                row.ProcessedAt = clock.UtcNowOffset();
                await db.SaveChangesAsync(ct);
                return;
            }
            var payload = JsonSerializer.Deserialize(message.Payload, eventType, ApiJson.Options)
                ?? throw new InvalidOperationException($"Outbox message {message.Id} has an empty payload.");
            var handlerType = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType);
            var handle = handlerType.GetMethod(nameof(IIntegrationEventHandler<IIntegrationEvent>.HandleAsync))!;

            foreach (var handler in services.GetServices(handlerType))
                await (Task)handle.Invoke(handler, [payload, ct])!;

            row.ProcessedAt = clock.UtcNowOffset();
            row.LastError = null;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            var cause = ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;
            row.Attempts++;
            row.LastError = cause.Message.Length > 2000 ? cause.Message[..2000] : cause.Message;
            Logger.Error(cause, "Outbox message {MessageId} ({EventType}) failed, attempt {Attempt}",
                message.Id, message.EventType, row.Attempts);
        }

        db.ChangeTracker.DetectChanges();
        await db.SaveChangesAsync(ct);
    }
}

public static class MessagingRegistration
{
    /// <summary>Called by a publishing module for its own DbContext.</summary>
    public static IServiceCollection AddOutboxDispatcher<TContext>(this IServiceCollection services)
        where TContext : ModuleDbContext
    {
        services.AddHostedService<OutboxDispatcher<TContext>>();
        return services;
    }

    /// <summary>Called by a consuming module for each handler.</summary>
    public static IServiceCollection AddIntegrationEventHandler<TEvent, THandler>(this IServiceCollection services)
        where TEvent : IIntegrationEvent
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        IntegrationEventNames.Of(typeof(TEvent)); // registers the wire name, and fails fast without the attribute
        services.AddScoped<IIntegrationEventHandler<TEvent>, THandler>();
        return services;
    }
}
