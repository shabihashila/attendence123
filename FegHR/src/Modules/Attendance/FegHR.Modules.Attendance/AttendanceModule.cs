using FegHR.Modules.Attendance.Features;
using FegHR.Modules.Attendance.Persistence;
using FegHR.SharedKernel.Modularity;
using FegHR.SharedKernel.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FegHR.Modules.Attendance;

/// <summary>
/// Attendance module: owns the <c>attendance</c> schema. Geo-fenced self-service punches with
/// a photo, the day log derived from them, HR review and correction, and the setup of work
/// locations, schedules and holidays.
/// </summary>
public sealed class AttendanceModule : IModule
{
    public string Name => "Attendance";
    public string Schema => AttendanceDbContext.SchemaName;
    public int Order => 40;

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<AttendanceDbContext>(configuration);
        services.AddScoped<AttendanceProcedures>();
    }

    public void MapEndpoints(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/attendance").WithTags(Name);
        SelfServiceEndpoints.Map(group);
        ManagementEndpoints.Map(group);
        SetupEndpoints.Map(group);
    }
}
