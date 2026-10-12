using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FegHR.SharedKernel.Tenancy;
using Microsoft.Extensions.Options;
using Serilog;
using Shared.Time;

namespace FegHR.SharedServices.Clients;

/// <summary>
/// Typed client for <c>FegStorage.WebApi</c> (<c>api/files/upload</c>,
/// <c>delete</c>, <c>view-url</c>). Ported from DailyCommerce's
/// <c>External.Clients.FileStorageClient</c>, with the storage target read
/// from configuration and every key confined to the caller's tenant.
/// Fails soft: problems come back as results, not exceptions.
/// </summary>
internal sealed class FileStorageClient(
    HttpClient http,
    IOptions<FileStorageServiceSettings> options,
    ITenantContext tenant,
    IAppTimeProvider clock) : IFileStorage
{
    private static readonly ILogger Logger = Log.ForContext("SourceContext", typeof(FileStorageClient).FullName);
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly FileStorageServiceSettings _settings = options.Value;

    public async Task<StoredFile> UploadAsync(string relativeKey, Stream content, string contentType,
        CancellationToken ct = default)
    {
        if (!TryBuildKey(relativeKey, out var key, out var problem))
            return Failed(relativeKey, problem);
        if (string.IsNullOrWhiteSpace(_settings.BaseUrl))
            return Failed(key, "FileStorageService.BaseUrl is not configured.");

        var meta = JsonSerializer.Serialize(new { config = Target(), key, contentType, viewUrlMinutes = 30 }, JsonOpts);
        using var form = new MultipartFormDataContent { { new StringContent(meta, Encoding.UTF8, "application/json"), "meta" } };
        var file = new StreamContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
        form.Add(file, "file", Path.GetFileName(key));

        try
        {
            using var response = await http.PostAsync("api/files/upload", form, ct);
            if (!response.IsSuccessStatusCode)
                return Failed(key, $"File storage returned {(int)response.StatusCode}.");

            var envelope = await response.Content.ReadFromJsonAsync<Envelope<UploadPayload>>(JsonOpts, ct);
            if (envelope?.Success != true || envelope.Data is null || envelope.Data.Key != key)
                return Failed(key, envelope?.Message ?? "File storage returned an unexpected response.");

            var data = envelope.Data;
            return new StoredFile(true, data.Key, data.SizeBytes, data.ContentType, data.ViewUrl, data.ViewUrlExpiresAt, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Logger.Warning(ex, "File upload failed for {Key}", key);
            return Failed(key, "File storage is unavailable.");
        }
    }

    public async Task<bool> DeleteAsync(string storedKey, CancellationToken ct = default)
    {
        if (!OwnsKey(storedKey) || string.IsNullOrWhiteSpace(_settings.BaseUrl))
            return false;
        try
        {
            using var response = await http.PostAsJsonAsync("api/files/delete", new { config = Target(), key = storedKey }, JsonOpts, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Logger.Warning(ex, "File delete failed for {Key}", storedKey);
            return false;
        }
    }

    public async Task<FileViewUrl?> GetViewUrlAsync(string storedKey, int minutes = 30, CancellationToken ct = default)
    {
        if (!OwnsKey(storedKey) || string.IsNullOrWhiteSpace(_settings.BaseUrl))
            return null;
        try
        {
            using var response = await http.PostAsJsonAsync("api/files/view-url",
                new { config = Target(), key = storedKey, viewUrlMinutes = minutes }, JsonOpts, ct);
            if (!response.IsSuccessStatusCode)
                return null;
            var envelope = await response.Content.ReadFromJsonAsync<Envelope<ViewUrlPayload>>(JsonOpts, ct);
            var data = envelope?.Data;
            if (envelope?.Success != true || data is null
                || !Uri.TryCreate(data.ViewUrl, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
                || data.ExpiresAt <= clock.UtcNowOffset())
                return null;
            return new FileViewUrl(data.ViewUrl, data.ExpiresAt);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Logger.Warning(ex, "View URL request failed for {Key}", storedKey);
            return null;
        }
    }

    private string TenantPrefix => $"tenants/{tenant.RequiredTenantId}/";

    private bool OwnsKey(string storedKey)
        => !string.IsNullOrWhiteSpace(storedKey) && tenant.TenantId is > 0
           && storedKey.StartsWith(TenantPrefix, StringComparison.Ordinal) && !storedKey.Contains("..");

    private bool TryBuildKey(string relativeKey, out string key, out string problem)
    {
        key = string.Empty;
        problem = string.Empty;
        var trimmed = (relativeKey ?? string.Empty).Replace('\\', '/').Trim().TrimStart('/');
        if (trimmed.Length == 0 || trimmed.Contains("..") || trimmed.Contains("//"))
        {
            problem = "The file key is not valid.";
            return false;
        }
        if (tenant.TenantId is not > 0)
        {
            problem = "No company is selected.";
            return false;
        }
        key = TenantPrefix + trimmed;
        return true;
    }

    private object Target()
    {
        var s = _settings.Storage;
        return new { provider = s.Provider, container = s.Container, local = s.Local, s3 = s.S3, azure = s.Azure };
    }

    private static StoredFile Failed(string key, string reason) => new(false, key, 0, string.Empty, null, null, reason);

    private sealed class Envelope<T>
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public T? Data { get; set; }
    }

    private sealed class UploadPayload
    {
        public string Key { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string ContentType { get; set; } = "application/octet-stream";
        public string? ViewUrl { get; set; }
        public DateTimeOffset? ViewUrlExpiresAt { get; set; }
    }

    private sealed class ViewUrlPayload
    {
        public string ViewUrl { get; set; } = string.Empty;
        public DateTimeOffset ExpiresAt { get; set; }
    }
}
