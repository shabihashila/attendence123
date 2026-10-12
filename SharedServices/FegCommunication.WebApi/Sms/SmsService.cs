// Author: Md. Sakibur Rahman
// Date:   2026-04-29
using FegCommunication.WebApi.Logs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Serilog;

namespace FegCommunication.WebApi.Sms;

// Resolves the right ISmsProvider per request: SendSmsRequest.Provider
// wins; otherwise SmsSettings.DefaultProvider. UnknownProviderException
// surfaces a 400-friendly error rather than a 500 -- the endpoint catches
// it and wraps the message in the ApiResponse envelope.
internal sealed class SmsService(
    IEnumerable<ISmsProvider> providers,
    IOptions<SmsSettings> opts,
    ICommunicationLogStore store,
    IHttpContextAccessor http) : ISmsService
{
    private readonly SmsSettings _s = opts.Value;
    private readonly Dictionary<string, ISmsProvider> _providersByName =
        providers.ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> KnownProviders => _providersByName.Keys;

    public async Task<SendSmsResult> SendAsync(SendSmsRequest req, CancellationToken ct = default)
    {
        var providerName = string.IsNullOrWhiteSpace(req.Provider) ? _s.DefaultProvider : req.Provider;
        if (!_providersByName.TryGetValue(providerName, out var provider))
            throw new UnknownSmsProviderException(providerName, _providersByName.Keys);

        var recipients = req.To
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct()
            .ToList();

        var result = new SendSmsResult
        {
            Total      = recipients.Count,
            Provider   = provider.Name,
            Recipients = recipients.Select(n => new SmsDeliveryRow { To = n }).ToList()
        };
        if (result.Total == 0) return result;

        var sender = string.IsNullOrWhiteSpace(req.SenderId) ? _s.SenderId : req.SenderId;
        var caller = http.HttpContext?.User?.Identity?.Name;

        // One provider call for the whole batch. Provider decides whether to
        // hit the gateway once (campaign mode) or fan out per recipient -- we
        // get a uniform per-recipient result list back either way.
        IReadOnlyList<SmsProviderResult> providerResults;
        try
        {
            providerResults = await provider.SendAsync(recipients, req.Message, sender, req.ContentId, req.Config, ct);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "SMS provider {Provider} threw for {Count} recipients", provider.Name, recipients.Count);
            // Fail the whole batch uniformly so every row still gets an audit entry.
            providerResults = recipients
                .Select(n => new SmsProviderResult(n, false, ex.Message, null))
                .ToList();
        }

        var byTo = providerResults.ToDictionary(r => r.To, StringComparer.OrdinalIgnoreCase);
        foreach (var row in result.Recipients)
        {
            if (byTo.TryGetValue(row.To, out var r))
            {
                row.Sent  = r.Ok;
                row.Error = r.Error;
                row.ProviderMessageId = r.ProviderMessageId;
            }
            else
            {
                // Defensive: a misbehaving provider returned fewer rows than
                // we asked for. Mark as failed so the caller doesn't think it
                // silently went through.
                row.Error = "Provider returned no result for this recipient.";
            }
            await store.LogSmsAsync(row.To, sender, row.Sent, row.Error, row.ProviderMessageId, provider.Name, caller, ct);
        }

        result.Sent   = result.Recipients.Count(r => r.Sent);
        result.Failed = result.Total - result.Sent;
        return result;
    }
}

public sealed class UnknownSmsProviderException(string requested, IEnumerable<string> known)
    : Exception($"Unknown SMS provider '{requested}'. Known providers: {string.Join(", ", known)}")
{
    public string Requested { get; } = requested;
    public IReadOnlyCollection<string> Known { get; } = known.ToArray();
}
