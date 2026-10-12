// Author: Md. Sakibur Rahman
// Date:   2026-04-29
using FegCommunication.WebApi.Auth;
using FegCommunication.WebApi.Email;
using FegCommunication.WebApi.Endpoints;
using FegCommunication.WebApi.Logs;
using FegCommunication.WebApi.Middlewares;
using FegCommunication.WebApi.Sms;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Serilog;
using Shared.Logging;

var builder = WebApplication.CreateBuilder(args);

// --- Serilog: shared bootstrap (console everything, SQL for our code only).
builder.AddSerilogLogging(builder.Configuration.GetConnectionString("DefaultConnection"), "logs", o =>
{
    o.AppNamespacePrefix = "FegCommunication";
    o.ServiceName = "FegCommunication";
    o.TableName = "Logs";
});

// --- Settings ---
builder.Services.Configure<ApiKeySettings>(builder.Configuration.GetSection("ApiKey"));
builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection("Smtp"));
builder.Services.Configure<SmsSettings>(builder.Configuration.GetSection("Sms"));

// --- Services ---
builder.Services.AddHttpContextAccessor();   // SmsService + EmailService read the API-key principal name for the audit row
builder.Services.AddScoped<ICommunicationLogStore, CommunicationLogStore>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<ISmsService, SmsService>();

// SMS providers. All registered up-front; SmsService picks the right
// one per request via SendSmsRequest.Provider (with Sms:DefaultProvider
// as fallback). Adding a new gateway = drop another ISmsProvider impl
// in here.
builder.Services.AddScoped<ISmsProvider, LogSmsProvider>();

builder.Services.Configure<AlphaSmsSettings>(builder.Configuration.GetSection("Sms:Providers:Alpha"));
builder.Services.AddHttpClient<AlphaSmsProvider>((sp, client) =>
{
    var opts = sp.GetRequiredService<IOptions<AlphaSmsSettings>>().Value;
    if (!string.IsNullOrWhiteSpace(opts.BaseUrl))
        client.BaseAddress = new Uri(opts.BaseUrl);
});
builder.Services.AddTransient<ISmsProvider>(sp => sp.GetRequiredService<AlphaSmsProvider>());

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

// Swagger / OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Communication Service API", Version = "v1" });

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

// Ensure the audit table exists before serving traffic. EnsureSchemaAsync
// is idempotent and a no-op when DefaultConnection isn't configured, so
// dev boxes without SQL Server still boot.
using (var scope = app.Services.CreateScope())
{
    var store = scope.ServiceProvider.GetRequiredService<ICommunicationLogStore>();
    await store.EnsureSchemaAsync();
}

// Same global exception envelope as HRAccounts -- consumers parsing the
// error response only need one shape.
app.UseMiddleware<GlobalExceptionMiddleware>();

// Swagger UI — available in every environment for now; lock behind IsDevelopment() before prod.
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Communication Service API v1");
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

app.MapEmailEndpoints();
app.MapSmsEndpoints();
app.MapLogsEndpoints();

app.MapGet("/", () => Results.Ok(new { status = "api is running", name = "FegCommunication API", environment = app.Environment.EnvironmentName }))
   .AllowAnonymous();

app.Run();
