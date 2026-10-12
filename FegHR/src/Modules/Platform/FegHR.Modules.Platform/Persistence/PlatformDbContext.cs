using FegHR.Modules.Platform.Domain;
using FegHR.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FegHR.Modules.Platform.Persistence;

/// <summary>
/// Maps the <c>platform</c> schema only. Tables are created by the versioned SQL scripts.
/// Companies, hosts and licences are reached through <see cref="PlatformProcedures"/>.
/// </summary>
internal sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "platform";

    protected override string Schema => SchemaName;

    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<TenantSetting> TenantSettings => Set<TenantSetting>();
    public DbSet<PlatformBranding> PlatformBranding => Set<PlatformBranding>();
    public DbSet<Plan> Plans => Set<Plan>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(PlatformDbContext).Assembly);
}
