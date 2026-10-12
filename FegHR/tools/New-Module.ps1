<#
.SYNOPSIS
  Scaffolds a FegHR module: the internal implementation project and
  its public .Contracts project, wired into the solution and the host.

.EXAMPLE
  .\tools\New-Module.ps1 -Name Catalog -Schema catalog -Order 30

.NOTES
  After running, add `new <Name>Module()` to the module list in
  src/Host/FegHR.Api/Program.cs. That list is explicit on purpose
  (ARCHITECTURE.md section 3, rule 6).
#>
param(
    [Parameter(Mandatory)] [ValidatePattern('^[A-Z][A-Za-z]+$')] [string] $Name,
    [Parameter(Mandatory)] [ValidatePattern('^[a-z]+$')] [string] $Schema,
    [string] $Route = $Schema,
    [int] $Order = 100
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$moduleDir = Join-Path $root "src\Modules\$Name"
$implName = "FegHR.Modules.$Name"
$contractsName = "$implName.Contracts"
$implDir = Join-Path $moduleDir $implName
$contractsDir = Join-Path $moduleDir $contractsName

if (Test-Path $moduleDir) { throw "Module folder already exists: $moduleDir" }

function Write-Utf8([string] $Path, [string] $Content) {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Path) | Out-Null
    [System.IO.File]::WriteAllText($Path, $Content.TrimStart(), (New-Object System.Text.UTF8Encoding $false))
}

Write-Utf8 (Join-Path $contractsDir "$contractsName.csproj") @"
<Project Sdk="Microsoft.NET.Sdk">

  <!-- The public surface of the $Name module: interfaces, DTOs and integration
       events other modules may use. No implementation and no persistence here. -->
  <PropertyGroup>
    <RootNamespace>$contractsName</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\..\BuildingBlocks\FegHR.SharedKernel\FegHR.SharedKernel.csproj" />
  </ItemGroup>

</Project>
"@

Write-Utf8 (Join-Path $implDir "$implName.csproj") @"
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <RootNamespace>$implName</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="FegHR.Tests" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\..\BuildingBlocks\FegHR.SharedKernel\FegHR.SharedKernel.csproj" />
    <ProjectReference Include="..\..\..\BuildingBlocks\FegHR.SharedServices.Clients\FegHR.SharedServices.Clients.csproj" />
    <ProjectReference Include="..\$contractsName\$contractsName.csproj" />
  </ItemGroup>

</Project>
"@

Write-Utf8 (Join-Path $implDir "${Name}Module.cs") @"
using FegHR.Modules.$Name.Persistence;
using FegHR.SharedKernel.Modularity;
using FegHR.SharedKernel.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace $implName;

/// <summary>$Name module: owns the <c>$Schema</c> schema.</summary>
public sealed class ${Name}Module : IModule
{
    public string Name => "$Name";
    public string Schema => ${Name}DbContext.SchemaName;
    public int Order => $Order;

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<${Name}DbContext>(configuration);
    }

    public void MapEndpoints(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/$Route").WithTags(Name);
        // Map each feature here, e.g. Features.Items.ItemEndpoints.Map(group);
        _ = group;
    }
}
"@

Write-Utf8 (Join-Path $implDir "Persistence\${Name}DbContext.cs") @"
using FegHR.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace $implName.Persistence;

/// <summary>Maps the <c>$Schema</c> schema only. Tables are created by the versioned SQL scripts.</summary>
internal sealed class ${Name}DbContext(DbContextOptions<${Name}DbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "$Schema";

    protected override string Schema => SchemaName;

    protected override void ConfigureModel(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(${Name}DbContext).Assembly);
}
"@

Push-Location $root
try {
    dotnet sln FegHR.slnx add --solution-folder "Modules/$Name" `
        (Join-Path $contractsDir "$contractsName.csproj") (Join-Path $implDir "$implName.csproj") | Out-Host
    dotnet add src\Host\FegHR.Api\FegHR.Api.csproj reference `
        (Join-Path $implDir "$implName.csproj") | Out-Host
}
finally { Pop-Location }

Write-Host ""
Write-Host "Created $implName and $contractsName."
Write-Host "Next: add 'new ${Name}Module()' to the module list in src/Host/FegHR.Api/Program.cs."
