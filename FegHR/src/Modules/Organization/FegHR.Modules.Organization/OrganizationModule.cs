using FegHR.Modules.Organization.Contracts;
using FegHR.Modules.Organization.Features;
using FegHR.Modules.Organization.Persistence;
using FegHR.SharedKernel.Modularity;
using FegHR.SharedKernel.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FegHR.Modules.Organization;

/// <summary>
/// Organization module: owns the <c>org</c> schema (departments, designations, employees) and
/// exposes <see cref="IEmployeeDirectory"/> to the attendance and leave modules.
/// </summary>
public sealed class OrganizationModule : IModule
{
    public string Name => "Organization";
    public string Schema => OrganizationDbContext.SchemaName;
    public int Order => 30;

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<OrganizationDbContext>(configuration);
        services.AddScoped<OrganizationProcedures>();
        services.AddScoped<IEmployeeDirectory, EmployeeDirectory>();
    }

    public void MapEndpoints(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/org").WithTags(Name);
        SetupEndpoints.Map(group);
        EmployeeEndpoints.Map(group);
    }
}
