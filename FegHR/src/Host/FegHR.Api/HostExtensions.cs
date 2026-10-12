using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using FegHR.SharedKernel.Http;
using FegHR.SharedKernel.Security;
using FegHR.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

namespace FegHR.Api;

internal static class HostExtensions
{
    public const string CorsPolicy = "FegHRUi";

    /// <summary>
    /// One bearer scheme for signed-in users. Inbound claim mapping is off, so
    /// <c>sub</c> and <c>tid</c> arrive verbatim.
    /// </summary>
    public static IServiceCollection AddFegHRAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var jwt = configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>() ?? new();
        if (Encoding.UTF8.GetByteCount(jwt.SigningKey) < 32)
            throw new InvalidOperationException(
                "Jwt:SigningKey must be at least 32 bytes. Set it in appsettings.Development.json or user-secrets " +
                "(see appsettings.Development.example.json), or in the server environment.");

        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = Audiences.Staff,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = ClaimNames.Name
                };
                // Denials use the response envelope like every other error, so the
                // UI interceptor can tell an expired session (401) from a missing right (403).
                options.Events = new JwtBearerEvents
                {
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await context.Response.WriteAsJsonAsync(
                            ApiResponse.Fail("Please sign in to continue."), ApiJson.Options);
                    },
                    OnForbidden = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return context.Response.WriteAsJsonAsync(
                            ApiResponse.Fail("You do not have access to this."), ApiJson.Options);
                    }
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(PolicyNames.Staff, p => p.RequireAuthenticatedUser().RequireClaim(ClaimNames.Audience, Audiences.Staff))
            .AddPolicy(PolicyNames.PlatformAdmin, p => p.RequireAuthenticatedUser()
                .RequireClaim(ClaimNames.Audience, Audiences.Staff)
                .RequireClaim(ClaimNames.PlatformAdmin, "1"));
        return services;
    }

    /// <summary>
    /// Trust X-Forwarded-For/-Proto only from the reverse proxies listed in configuration
    /// (<c>ReverseProxy:KnownProxies</c> IP addresses, <c>ReverseProxy:KnownNetworks</c> CIDR ranges).
    /// With nothing listed, forwarded headers are ignored and the connection address is used.
    /// </summary>
    public static IServiceCollection AddFegHRForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        var proxies = configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [];
        var networks = configuration.GetSection("ReverseProxy:KnownNetworks").Get<string[]>() ?? [];
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.ForwardLimit = 1;
            o.KnownProxies.Clear();
            o.KnownIPNetworks.Clear();
            foreach (var proxy in proxies)
                o.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));
            foreach (var network in networks)
                o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
        });
        return services;
    }

    public static IServiceCollection AddFegHRCors(this IServiceCollection services, IConfiguration configuration)
    {
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(options => options.AddPolicy(CorsPolicy, policy =>
        {
            if (origins.Length > 0)
                policy.WithOrigins(origins)
                    .AllowAnyMethod()
                    .WithHeaders("Authorization", "Content-Type", TenancyHeaders.Tenant, TenancyHeaders.Branch, "X-Requested-With")
                    .WithExposedHeaders("Retry-After");
        }));
        return services;
    }

    /// <summary>
    /// Sign-in allows 5 attempts per minute and company registration 5 per ten minutes, per client IP.
    /// A rejection returns the standard envelope, so the UI shows the message like any other error.
    /// </summary>
    public static IServiceCollection AddFegHRRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(RateLimitPolicies.Login, http => PerIp(http, 5, TimeSpan.FromMinutes(1)));
            limiter.AddPolicy(RateLimitPolicies.Registration, http => PerIp(http, 5, TimeSpan.FromMinutes(10)));
            limiter.OnRejected = async (context, ct) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
                    context.HttpContext.Response.Headers.RetryAfter = ((int)retry.TotalSeconds).ToString();
                context.HttpContext.Response.ContentType = "application/json";
                await context.HttpContext.Response.WriteAsync(JsonSerializer.Serialize(
                    ApiResponse.Fail("Too many attempts. Please wait a little and try again."), ApiJson.Options), ct);
            };
        });
        return services;
    }

    private static RateLimitPartition<string> PerIp(HttpContext http, int permits, TimeSpan window)
        => RateLimitPartition.GetFixedWindowLimiter(http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                Window = window,
                PermitLimit = permits,
                QueueLimit = 0,
                AutoReplenishment = true
            });

    public static IServiceCollection AddFegHRSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = "FegHR API", Version = "v1" });
            // Modules may reuse simple type names (CreateRequest); full names keep schema ids unique.
            c.CustomSchemaIds(type => type.FullName?.Replace('+', '.'));
            c.AddSecurityDefinition(JwtBearerDefaults.AuthenticationScheme, new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Description = "JWT bearer token",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT"
            });
            c.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
            {
                { new OpenApiSecuritySchemeReference(JwtBearerDefaults.AuthenticationScheme, doc, null!), new List<string>() }
            });
        });
        return services;
    }
}

/// <summary>Readiness: the database answers a round trip on a session-bound connection.</summary>
internal sealed class DatabaseHealthCheck(ITenantDbConnectionFactory connections) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await using var connection = await connections.OpenAsync(ct);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database unreachable.", ex);
        }
    }
}
