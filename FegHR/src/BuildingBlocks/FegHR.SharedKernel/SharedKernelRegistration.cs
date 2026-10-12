using FegHR.SharedKernel.Messaging;
using FegHR.SharedKernel.Persistence;
using FegHR.SharedKernel.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shared.Time;

namespace FegHR.SharedKernel;

public static class SharedKernelRegistration
{
    /// <summary>Tenancy, persistence plumbing, messaging and the application clock.</summary>
    public static IServiceCollection AddSharedKernel(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSharedTime();
        services.Configure<TenancyOptions>(configuration.GetSection(TenancyOptions.SectionName));

        services.TryAddScoped<TenantContext>();
        services.TryAddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.TryAddScoped<ITenantDbConnectionFactory, TenantDbConnectionFactory>();
        services.TryAddScoped<TenantConnectionInterceptor>();
        services.TryAddScoped<AuditStampInterceptor>();
        services.TryAddScoped<IOutbox, Outbox>();
        return services;
    }
}
