using FegHR.SharedKernel.Messaging;
using FegHR.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FegHR.SharedKernel.Persistence;

/// <summary>
/// Base for every module's DbContext. Maps into the module's own schema,
/// applies the row-version convention, and maps the module's outbox and inbox
/// tables. The schema is created by the versioned SQL scripts; EF never
/// creates or migrates anything.
/// </summary>
public abstract class ModuleDbContext(DbContextOptions options) : DbContext(options)
{
    /// <summary>The SQL schema this module owns. Must match <c>IModule.Schema</c>.</summary>
    protected abstract string Schema { get; }

    /// <summary>Whether this module publishes or consumes integration events.</summary>
    protected virtual bool UsesMessaging => false;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        if (UsesMessaging)
        {
            modelBuilder.Entity<OutboxMessage>(OutboxMessage.Configure);
            modelBuilder.Entity<InboxMessage>(InboxMessage.Configure);
        }

        ConfigureModel(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(t => typeof(IVersioned).IsAssignableFrom(t.ClrType)))
        {
            modelBuilder.Entity(entityType.ClrType).Property(nameof(IVersioned.RowVersion)).IsRowVersion();
        }
    }

    /// <summary>Module mappings. Prefer <c>ApplyConfigurationsFromAssembly</c> over inline configuration.</summary>
    protected abstract void ConfigureModel(ModelBuilder modelBuilder);
}

public static class ModuleDbContextRegistration
{
    /// <summary>
    /// Registers a module DbContext on the shared connection string with the
    /// tenant session binding and audit stamping interceptors.
    /// </summary>
    public static IServiceCollection AddModuleDbContext<TContext>(this IServiceCollection services,
        IConfiguration configuration) where TContext : ModuleDbContext
    {
        var connectionString = configuration.GetConnectionString(DatabaseNames.ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{DatabaseNames.ConnectionStringName}' is not configured.");

        services.AddDbContext<TContext>((sp, options) =>
        {
            options.UseSqlServer(connectionString);
            options.AddInterceptors(
                sp.GetRequiredService<TenantConnectionInterceptor>(),
                sp.GetRequiredService<AuditStampInterceptor>());
        });
        return services;
    }
}
