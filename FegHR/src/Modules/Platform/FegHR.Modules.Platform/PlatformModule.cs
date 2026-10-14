using FegHR.Modules.Platform.Contracts;
using FegHR.Modules.Platform.Features.Branches;
using FegHR.Modules.Platform.Features.Branding;
using FegHR.Modules.Platform.Features.Licensing;
using FegHR.Modules.Platform.Features.Media;
using FegHR.Modules.Platform.Features.Tenant;
using FegHR.Modules.Platform.Persistence;
using FegHR.Modules.Platform.Tenancy;
using FegHR.SharedKernel.Modularity;
using FegHR.SharedKernel.Persistence;
using FegHR.SharedKernel.Security;
using FegHR.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FegHR.Modules.Platform;

/// <summary>
/// Platform module: owns the <c>platform</c> schema (companies, branches, settings, plans,
/// licences, product branding). Hosts the licence guard's verdict, the product owner console
/// and exposes <see cref="ITenantDirectory"/> to other modules.
/// </summary>
public sealed class PlatformModule : IModule
{
    public string Name => "Platform";
    public string Schema => PlatformDbContext.SchemaName;
    public int Order => 10;

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<PlatformDbContext>(configuration);
        services.Configure<PlatformTenancyOptions>(configuration.GetSection("Tenancy"));
        services.AddScoped<PlatformProcedures>();
        services.AddScoped<ITenantHostResolver, TenantHostResolver>();
        services.AddScoped<ITenantDirectory, TenantDirectory>();
        services.AddScoped<ILicenseGate, LicenseGate>();
        services.AddMemoryCache();
    }

    public void MapEndpoints(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/platform").WithTags(Name);
        TenantEndpoints.Map(group, routes);
        GroupEndpoints.Map(routes);
        BranchEndpoints.Map(group);
        BrandingEndpoints.Map(group, routes);
        PlatformBrandingEndpoints.Map(routes);
        LicenseEndpoints.Map(group, routes);
        MediaEndpoints.Map(routes);
    }
}
