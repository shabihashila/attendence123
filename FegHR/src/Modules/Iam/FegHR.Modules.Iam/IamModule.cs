using FegHR.Modules.Iam.Features.Auth;
using FegHR.Modules.Iam.Contracts;
using FegHR.Modules.Iam.Features.Me;
using FegHR.Modules.Iam.Features.Registration;
using FegHR.Modules.Iam.Features.Roles;
using FegHR.Modules.Iam.Features.Users;
using FegHR.Modules.Iam.Persistence;
using FegHR.Modules.Iam.Security;
using FegHR.SharedKernel.Modularity;
using FegHR.SharedKernel.Persistence;
using FegHR.SharedKernel.Security;
using FegHR.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FegHR.Modules.Iam;

/// <summary>
/// Iam module (identity and access management): owns the <c>iam</c> schema. Public company
/// registration, sign-in and token rotation, the permission and branch checks the shared kernel
/// relies on, the dynamic navigation tree, and user/role management.
/// </summary>
public sealed class IamModule : IModule
{
    public string Name => "Iam";
    public string Schema => IamDbContext.SchemaName;
    public int Order => 20;

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<IamDbContext>(configuration);
        services.AddScoped<IamProcedures>();
        services.AddSingleton<TokenService>();
        services.AddSingleton<PermissionGenerations>();
        services.AddScoped<IPermissionProvider, PermissionProvider>();
        services.AddScoped<BranchAccessPolicy>();
        services.AddScoped<IBranchAccessPolicy>(sp => sp.GetRequiredService<BranchAccessPolicy>());
        services.AddScoped<AuthService>();
        services.AddScoped<DelegationGuard>();
        services.AddScoped<IMemberDirectory, MemberDirectory>();
    }

    public void MapEndpoints(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/iam").WithTags(Name);
        AuthEndpoints.Map(group);
        RegistrationEndpoints.Map(group);
        MeEndpoints.Map(group);
        RoleEndpoints.Map(group);
        UserEndpoints.Map(group);
    }
}
