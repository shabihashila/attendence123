using System.Data;
using Dapper;
using FegHR.Modules.Organization.Contracts;
using FegHR.Modules.Platform.Contracts;
using FegHR.SharedKernel.Http;
using FegHR.SharedKernel.Modularity;
using FegHR.SharedKernel.Security;
using FegHR.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Time;

namespace FegHR.Modules.Reporting;

/// <summary>Today's numbers for the home page. Company-wide figures are shown to people who may see them.</summary>
internal sealed record DashboardDto(
    string Date, string? EmployeeName, bool HasCompanyView, int Headcount, int Present, int Late, int Absent, int OnLeave,
    int PendingLeave, int PunchesToReview, LicenseNotice? Licence);

internal sealed record LicenseNotice(string? Kind, string? PlanName, DateTimeOffset? EndsAt, int? DaysLeft);

internal sealed class DashboardSummary
{
    public int Headcount { get; set; }
    public int Present { get; set; }
    public int Late { get; set; }
    public int Absent { get; set; }
    public int OnLeave { get; set; }
    public int PendingLeave { get; set; }
    public int PunchesToReview { get; set; }
}

/// <summary>
/// Reporting module: read-only procedures of the <c>reporting</c> schema, the one schema that
/// may read across module schemas. The attendance and leave reports are served by their own
/// modules; this one hosts what belongs to no single module, the dashboard.
/// </summary>
public sealed class ReportingModule : IModule
{
    private const string AttendanceLogsRead = "attendance.logs.read";

    public string Name => "Reporting";
    public string Schema => "reporting";
    public int Order => 90;

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/reporting/dashboard", async (ITenantContext tenant, ITenantDbConnectionFactory connections,
                ITenantDirectory tenants, IEmployeeDirectory employees, IPermissionProvider permissions, IAppTimeProvider clock,
                CancellationToken ct) =>
            {
                var now = clock.UtcNowOffset();
                var today = (await tenants.GetClockAsync(ct)).DateOf(now);
                var granted = await permissions.GetPermissionsAsync(tenant.RequiredTenantId, tenant.UserId!.Value, ct);
                var companyView = granted.Contains(AttendanceLogsRead);

                var summary = new DashboardSummary();
                if (companyView)
                {
                    await using var connection = await connections.OpenAsync(ct);
                    summary = await connection.QuerySingleAsync<DashboardSummary>(new CommandDefinition(
                        "reporting.usp_Dashboard_Summary", new { Today = today.ToDateTime(TimeOnly.MinValue) },
                        commandType: CommandType.StoredProcedure, cancellationToken: ct));
                }

                var licence = await tenants.GetLicenseStatusAsync(ct);
                var me = await employees.GetByUserAsync(tenant.UserId.Value, ct);
                return ApiResults.Ok(new DashboardDto(today.ToString("yyyy-MM-dd"), me?.FullName, companyView,
                    summary.Headcount, summary.Present, summary.Late, summary.Absent, summary.OnLeave, summary.PendingLeave,
                    summary.PunchesToReview,
                    licence is null ? null : new LicenseNotice(licence.Kind, licence.PlanName, licence.EndsAt, licence.DaysLeft(now))));
            })
            .RequireStaff()
            .WithTags(Name)
            .WithName("GetDashboard");
    }
}
