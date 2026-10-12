using FegHR.Modules.Iam.Domain;
using FegHR.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FegHR.Modules.Iam.Persistence;

/// <summary>
/// Maps the <c>iam</c> schema only. Tables are created by the versioned SQL scripts.
/// Global users and menus are reached through <see cref="IamProcedures"/>; the
/// application role cannot read <c>iam.Users</c> directly.
/// </summary>
internal sealed class IamDbContext(DbContextOptions<IamDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "iam";

    protected override string Schema => SchemaName;

    public DbSet<TenantUser> TenantUsers => Set<TenantUser>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<UserBranch> UserBranches => Set<UserBranch>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Permission> Permissions => Set<Permission>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(IamDbContext).Assembly);
}
