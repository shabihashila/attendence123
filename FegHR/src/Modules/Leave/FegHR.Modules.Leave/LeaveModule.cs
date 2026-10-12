using FegHR.Modules.Leave.Features;
using FegHR.Modules.Leave.Persistence;
using FegHR.SharedKernel.Modularity;
using FegHR.SharedKernel.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FegHR.Modules.Leave;

/// <summary>
/// Leave module: owns the <c>leave</c> schema. Leave types, applications with two approval
/// steps, balance adjustments and the leave statement.
/// </summary>
public sealed class LeaveModule : IModule
{
    public string Name => "Leave";
    public string Schema => LeaveDbContext.SchemaName;
    public int Order => 50;

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<LeaveDbContext>(configuration);
        services.AddScoped<LeaveProcedures>();
    }

    public void MapEndpoints(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/leave").WithTags(Name);
        LeaveEndpoints.Map(group);
    }
}
