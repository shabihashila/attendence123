using Microsoft.Extensions.DependencyInjection;

namespace FegHR.SharedKernel.Tenancy;

/// <summary>
/// Creates a DI scope bound to an explicit tenant, for work that must act as a
/// tenant the request is not (yet) bound to: issuing tokens during sign-in,
/// rotating a refresh token, background jobs. Everything resolved from the
/// scope (DbContexts, Dapper connections) is RLS-filtered to that tenant.
/// </summary>
public static class TenantScopes
{
    public static AsyncServiceScope CreateTenantScope(this IServiceScopeFactory scopes, int tenantId, int? userId = null)
    {
        if (tenantId <= 0)
            throw new ArgumentOutOfRangeException(nameof(tenantId));
        var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>()
            .Bind(tenantId, userId, null, false, TenantSource.BackgroundScope);
        return scope;
    }
}
