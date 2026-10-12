using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FegHR.SharedServices.Clients;

public static class SharedServicesRegistration
{
    /// <summary>
    /// Registers the FileStorage and Communication typed clients once. The
    /// guard matters: repeated <c>AddHttpClient</c> configuration callbacks
    /// would each add another X-Api-Key header, and the services reject that
    /// (the same issue DailyCommerce hit).
    /// </summary>
    public static IServiceCollection AddSharedServiceClients(this IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(d => d.ServiceType == typeof(Registered)))
            return services;
        services.AddSingleton<Registered>();

        services.Configure<FileStorageServiceSettings>(configuration.GetSection(FileStorageServiceSettings.SectionName));
        services.AddHttpClient<FileStorageClient>(client =>
        {
            var s = configuration.GetSection(FileStorageServiceSettings.SectionName).Get<FileStorageServiceSettings>() ?? new();
            Configure(client, s.BaseUrl, s.ApiKey, s.TimeoutSeconds);
        });
        services.TryAddScoped<IFileStorage>(sp => sp.GetRequiredService<FileStorageClient>());

        services.Configure<CommunicationServiceSettings>(configuration.GetSection(CommunicationServiceSettings.SectionName));
        services.AddHttpClient<CommunicationClient>(client =>
        {
            var s = configuration.GetSection(CommunicationServiceSettings.SectionName).Get<CommunicationServiceSettings>() ?? new();
            Configure(client, s.BaseUrl, s.ApiKey, s.TimeoutSeconds);
        });
        services.TryAddScoped<ISmsSender>(sp => sp.GetRequiredService<CommunicationClient>());
        services.TryAddScoped<IEmailSender>(sp => sp.GetRequiredService<CommunicationClient>());

        return services;
    }

    private static void Configure(HttpClient client, string baseUrl, string apiKey, int timeoutSeconds)
    {
        if (!string.IsNullOrWhiteSpace(baseUrl))
            client.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
        client.Timeout = TimeSpan.FromSeconds(timeoutSeconds > 0 ? timeoutSeconds : 30);
        if (!string.IsNullOrWhiteSpace(apiKey))
            client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
    }

    private sealed class Registered;
}
