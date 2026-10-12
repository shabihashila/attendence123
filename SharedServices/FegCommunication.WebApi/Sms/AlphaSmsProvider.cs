// Author: Md. Sakibur Rahman
// Date:   2026-05-14
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Serilog;

namespace FegCommunication.WebApi.Sms;

// HTTP-based SMS provider for Alpha (sms.net.bd-compatible) gateway.
// Wire format: GET /sendsms?api_key=...&to=...&msg=...&sender_id=...
// Response (snake_case): { "error": 0, "msg": "...", "data": { "request_id": N } }
//
// Imported from the legacy SalesSmsAPI project as the first real provider
// behind ISmsProvider. The legacy code stored an AES-encrypted ApiKey in
// appsettings with a hardcoded key -- this version reads the ApiKey
// directly so deployments can pass it via env var or User Secrets instead.
internal sealed class AlphaSmsProvider(
    HttpClient http,
    IOptions<AlphaSmsSettings> opts) : ISmsProvider
{
    private readonly AlphaSmsSettings _s = opts.Value;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public string Name => "Alpha";

    public async Task<IReadOnlyList<SmsProviderResult>> SendAsync(
        IReadOnlyList<string> to, string message, string senderId, string? contentId,
        SmsBackendConfig? config, CancellationToken ct)
    {
        if (to.Count == 0) return Array.Empty<SmsProviderResult>();

        // Inline tenant config wins over the IOptions fallback. The fallback
        // path is still here so callers that haven't migrated to per-tenant
        // config (HomeoCare today) continue working unchanged.
        var apiKey  = !string.IsNullOrWhiteSpace(config?.Alpha?.ApiKey)  ? config!.Alpha!.ApiKey  : _s.ApiKey;
        var baseUrl = !string.IsNullOrWhiteSpace(config?.Alpha?.BaseUrl) ? config!.Alpha!.BaseUrl : null;

        if (string.IsNullOrWhiteSpace(apiKey))
            return FailAll(to, "Alpha SMS not configured: pass SmsBackendConfig.Alpha.ApiKey, or set Sms:Providers:Alpha:ApiKey on the server.");
        if (string.IsNullOrWhiteSpace(baseUrl) && http.BaseAddress is null)
            return FailAll(to, "Alpha SMS not configured: pass SmsBackendConfig.Alpha.BaseUrl, or set Sms:Providers:Alpha:BaseUrl on the server.");

        // sms.net.bd: the comma-joined `to=` form is "applicable for only
        // campaign SMS" and campaign SMS requires content_id. So we batch
        // when both conditions hold; otherwise we fall back to per-recipient
        // calls (the only documented path in that case). Single-recipient
        // requests also take the per-recipient path -- no point pretending
        // it's a campaign.
        var canBatch = to.Count > 1 && !string.IsNullOrWhiteSpace(contentId);
        if (canBatch)
        {
            var (ok, err, requestId) = await SendOnceAsync(
                string.Join(",", to), message, senderId, contentId, apiKey!, baseUrl, ct);
            // Gateway returns a single request_id for the whole batch; we
            // replicate it across rows so the audit log stays uniform.
            return to.Select(n => new SmsProviderResult(n, ok, err, requestId)).ToList();
        }

        var results = new List<SmsProviderResult>(to.Count);
        foreach (var number in to)
        {
            var (ok, err, requestId) = await SendOnceAsync(
                number, message, senderId, contentId, apiKey!, baseUrl, ct);
            results.Add(new SmsProviderResult(number, ok, err, requestId));
        }
        return results;
    }

    private async Task<(bool Ok, string? Error, string? ProviderMessageId)> SendOnceAsync(
        string toParam, string message, string senderId, string? contentId,
        string apiKey, string? baseUrl, CancellationToken ct)
    {
        var parameters = new Dictionary<string, string>
        {
            ["api_key"] = apiKey,
            ["msg"]     = message,
            ["to"]      = toParam
        };
        if (!string.IsNullOrWhiteSpace(senderId))
            parameters["sender_id"] = senderId;
        // BD regulators: non-OTP commercial bulk SMS needs a pre-approved
        // content_id. Optional on the gateway for single sends, required for
        // campaign (comma-joined) sends.
        if (!string.IsNullOrWhiteSpace(contentId))
            parameters["content_id"] = contentId;

        // When the caller overrides BaseUrl per request, build an absolute
        // URI so we bypass the typed client's fixed BaseAddress without
        // mutating it (the client is shared across all requests).
        var url = string.IsNullOrWhiteSpace(baseUrl)
            ? "/sendsms?" + BuildQueryString(parameters)
            : baseUrl!.TrimEnd('/') + "/sendsms?" + BuildQueryString(parameters);

        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var alphaResp = await JsonSerializer.DeserializeAsync<AlphaSmsApiResponse>(stream, JsonOpts, ct);

            if (alphaResp is null)
                return (false, $"Empty response from Alpha SMS (HTTP {(int)response.StatusCode})", null);

            // Gateway uses error=0 for success. Non-zero comes back with a
            // human-readable Msg explaining why (e.g. invalid number, low
            // balance, missing sender_id approval).
            if (alphaResp.Error != 0)
                return (false, alphaResp.Msg ?? $"Alpha SMS error code {alphaResp.Error}", null);

            return (true, null, alphaResp.Data?.RequestId.ToString());
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Alpha SMS send failed for {To}", toParam);
            return (false, ex.Message, null);
        }
    }

    private static IReadOnlyList<SmsProviderResult> FailAll(IReadOnlyList<string> to, string error) =>
        to.Select(n => new SmsProviderResult(n, false, error, null)).ToList();

    private static string BuildQueryString(Dictionary<string, string> parameters) =>
        string.Join("&", parameters.Select(kvp =>
            $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

    private sealed class AlphaSmsApiResponse
    {
        public int Error { get; set; }
        public string? Msg { get; set; }
        public AlphaSmsApiData? Data { get; set; }
    }

    private sealed class AlphaSmsApiData
    {
        [JsonPropertyName("request_id")]
        public int RequestId { get; set; }
    }
}
