using Microsoft.AspNetCore.HttpOverrides;
using FegHR.Api;
using FegHR.Modules.Attendance;
using FegHR.Modules.Iam;
using FegHR.Modules.Leave;
using FegHR.Modules.Organization;
using FegHR.Modules.Platform;
using FegHR.Modules.Platform.Tenancy;
using FegHR.Modules.Reporting;
using FegHR.SharedKernel;
using FegHR.SharedKernel.Http;
using FegHR.SharedKernel.Modularity;
using FegHR.SharedKernel.Security;
using FegHR.SharedKernel.Tenancy;
using FegHR.SharedServices.Clients;
using Serilog;
using Shared.Logging;

var builder = WebApplication.CreateBuilder(args);

// An unset environment defaults to Production. Refuse to start in that state
// against a remote database, so running the built DLL on a developer machine
// can never write to a live server (same guard as DailyCommerce). It runs
// before logging, because the SQL log sink writes to that database.
var environmentWasChosen =
    !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"))
    || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"));
var connectionString = builder.Configuration.GetConnectionString(DatabaseNames.ConnectionStringName);
if (!environmentWasChosen && builder.Environment.IsProduction() && IsRemoteSqlServer(connectionString))
    throw new InvalidOperationException(
        "Refusing to start: ASPNETCORE_ENVIRONMENT is not set and DefaultConnection points at a remote server. " +
        "Set ASPNETCORE_ENVIRONMENT explicitly.");

// The logs table is created by the versioned scripts (the app login has no
// DDL rights). Logging:WriteToDatabase=false keeps console logging only, for
// running before the database exists.
var logConnection = builder.Configuration.GetValue("Logging:WriteToDatabase", true) ? connectionString : null;
builder.AddSerilogLogging(logConnection, builder.Configuration["Logging:SqlSchema"], o =>
{
    o.AppNamespacePrefix = "FegHR";
    o.ServiceName = "FegHR.Api";
});

// ---------------------------------------------------------------------------
// Modules: an explicit, ordered list. A module joining the application is a
// visible one-line change here, never a side effect of assembly scanning
// (docs/feghr/ARCHITECTURE.md section 3, rule 6).
// ---------------------------------------------------------------------------
var modules = new IModule[]
{
    new PlatformModule(),
    new IamModule(),
    new OrganizationModule(),
    new AttendanceModule(),
    new LeaveModule(),
    new ReportingModule()
}.OrderBy(m => m.Order).ToArray();

builder.Services.AddSharedKernel(builder.Configuration);
builder.Services.AddSharedServiceClients(builder.Configuration);
foreach (var module in modules)
    module.RegisterServices(builder.Services, builder.Configuration);

builder.Services.AddFegHRAuth(builder.Configuration);
builder.Services.AddFegHRCors(builder.Configuration);
builder.Services.AddFegHRForwardedHeaders(builder.Configuration);
builder.Services.AddFegHRRateLimiting();
builder.Services.AddFegHRSwagger();
builder.Services.AddMemoryCache();
builder.Services.ConfigureHttpJsonOptions(o => ApiJson.Configure(o.SerializerOptions));
builder.Services.AddResponseCompression(o => o.EnableForHttps = true);
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

var app = builder.Build();

// Behind a reverse proxy the client address and scheme come from X-Forwarded-For/-Proto, so rate
// limits and the punch IP see the real client. Only the proxies listed in ReverseProxy:* are trusted.
var forwarding = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ForwardedHeadersOptions>>().Value;
if (forwarding.KnownProxies.Count > 0 || forwarding.KnownIPNetworks.Count > 0)
    app.UseForwardedHeaders();

// Request logging wraps the exception handler, so a refused request is logged with the status the
// client received (422, 404, ...) and only real failures are logged as errors.
app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseResponseCompression();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "FegHR API v1"));
}

app.UseRouting();
app.UseCors(HostExtensions.CorsPolicy);
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseMiddleware<CompanyAuditMiddleware>();
app.UseAuthorization();
app.UseMiddleware<LicenseGuardMiddleware>();
if (app.Environment.IsProduction())
    app.UseRateLimiter();

foreach (var module in modules)
{
    module.MapEndpoints(app);
    Log.Information("Module {Module} mapped (schema {Schema})", module.Name, module.Schema);
}

app.MapHealthChecks("/health", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") });
app.MapGet("/", () => ApiResults.Ok("FegHR API is running")).ExcludeFromDescription();

app.Run();

// True when the connection string targets another machine. Used only by the
// unset-environment guard above.
static bool IsRemoteSqlServer(string? connectionString)
{
    if (string.IsNullOrWhiteSpace(connectionString))
        return false;

    var source = connectionString
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(part => part.Split('=', 2))
        .Where(pair => pair.Length == 2 && pair[0].Trim() is var key
            && (key.Equals("Data Source", StringComparison.OrdinalIgnoreCase)
                || key.Equals("Server", StringComparison.OrdinalIgnoreCase)
                || key.Equals("Address", StringComparison.OrdinalIgnoreCase)))
        .Select(pair => pair[1].Trim())
        .FirstOrDefault();
    if (string.IsNullOrWhiteSpace(source))
        return false;

    var host = source.Split(',')[0].Split('\\')[0].Trim();
    if (host.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
        host = host[4..];
    return host.Length > 0
        && host is not "." and not "127.0.0.1" and not "::1"
        && !host.Equals("(local)", StringComparison.OrdinalIgnoreCase)
        && !host.StartsWith("(localdb)", StringComparison.OrdinalIgnoreCase)
        && !host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        && !host.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase);
}
