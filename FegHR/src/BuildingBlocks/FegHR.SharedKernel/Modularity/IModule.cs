using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FegHR.SharedKernel.Modularity;

/// <summary>
/// One bounded context of the FegHR monolith. A module owns one SQL
/// schema, one DbContext and its endpoint groups, and exposes nothing else
/// except the interfaces in its <c>.Contracts</c> project.
/// <para>
/// The host keeps an explicit, ordered list of modules in <c>Program.cs</c>
/// instead of scanning assemblies, the same wiring as DailyCommerce and ArchFirm.
/// </para>
/// </summary>
public interface IModule
{
    /// <summary>Display name, used in logs and as the OpenAPI tag.</summary>
    string Name { get; }

    /// <summary>The SQL schema this module owns.</summary>
    string Schema { get; }

    /// <summary>Registration order. Lower runs first; platform and iam go first.</summary>
    int Order => 100;

    void RegisterServices(IServiceCollection services, IConfiguration configuration);

    void MapEndpoints(IEndpointRouteBuilder routes);
}
