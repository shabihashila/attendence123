// Author: Md. Sakibur Rahman
// Date:   2026-05-13
using FegStorage.WebApi.Auth;
using FegStorage.WebApi.Endpoints;
using FegStorage.WebApi.Logs;
using FegStorage.WebApi.Middlewares;
using FegStorage.WebApi.Storage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.OpenApi;
using Serilog;
using Shared.Logging;

var builder = WebApplication.CreateBuilder(args);

// --- Serilog: shared bootstrap (console everything, SQL for our code only).
builder.AddSerilogLogging(builder.Configuration.GetConnectionString("DefaultConnection"), "logs", o =>
{
    o.AppNamespacePrefix = "FegStorage";
    o.ServiceName = "FegStorage";
    o.TableName = "Logs";
});

// --- Settings ---
builder.Services.Configure<ApiKeySettings>(builder.Configuration.GetSection("ApiKey"));
builder.Services.Configure<LocalBackendSettings>(builder.Configuration.GetSection("LocalBackend"));
builder.Services.Configure<UploadLimits>(opts =>
    opts.MaxUploadBytes = builder.Configuration.GetValue<long?>("MaxUploadBytes") ?? 52_428_800);

// --- Storage + audit services ---
builder.Services.AddSingleton<LocalUrlSigner>();
builder.Services.AddSingleton<IStorageBackendFactory, StorageBackendFactory>();
builder.Services.AddScoped<IFileOperationLogStore, FileOperationLogStore>();
builder.Services.AddHttpContextAccessor();

// --- Auth: ApiKey scheme ---
builder.Services
    .AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization();

// --- Minimal API JSON + Swagger ---
builder.Services.ConfigureHttpJsonOptions(opts =>
{
    opts.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});

// Multipart upload caps. Mirrored on Kestrel + the form binder so a
// big payslip PDF doesn't get clipped by an unrelated limit.
var maxUpload = builder.Configuration.GetValue<long?>("MaxUploadBytes") ?? 52_428_800L;
builder.Services.Configure<FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = maxUpload;
    o.ValueLengthLimit         = int.MaxValue;
});
builder.WebHost.ConfigureKestrel(o =>
{
    o.Limits.MaxRequestBodySize = maxUpload;
});

// Swagger / OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "File Storage Service API", Version = "v1" });

    c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Name = "X-Api-Key",
        Description = "API key shared with the calling service (HRAccounts, etc.)",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
    });

    c.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
    {
        { new OpenApiSecuritySchemeReference("ApiKey", doc, null!), new List<string>() },
    });
});

var app = builder.Build();

// Ensure the audit table exists before serving traffic. Idempotent +
// a no-op when DefaultConnection isn't configured.
using (var scope = app.Services.CreateScope())
{
    var store = scope.ServiceProvider.GetRequiredService<IFileOperationLogStore>();
    await store.EnsureSchemaAsync();
}

app.UseMiddleware<GlobalExceptionMiddleware>();

// Swagger UI — available in every environment for now; lock behind IsDevelopment() before prod.
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "File Storage Service API v1");
    c.RoutePrefix = "swagger";
});

// Don't redirect HTTP→HTTPS in development — Swagger UI loaded over HTTP
// then fetches /swagger/v1/swagger.json, the 307 to the (often-untrusted)
// HTTPS port fails silently, and the UI reports "no version field".
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseAuthentication();
app.UseAuthorization();

app.MapFilesEndpoints();
app.MapLocalFileViewEndpoints();
app.MapLogsEndpoints();

app.MapGet("/", () => Results.Ok(new { status = "api is running", name = "FegStorage API", environment = app.Environment.EnvironmentName }))
   .AllowAnonymous();

app.Run();
