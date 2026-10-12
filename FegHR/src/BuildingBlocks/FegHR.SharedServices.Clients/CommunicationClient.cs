using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Serilog;

namespace FegHR.SharedServices.Clients;

/// <summary>
/// Typed client for <c>FegCommunication.WebApi</c> (<c>api/sms/send</c>,
/// <c>api/email/send</c>), ported from DailyCommerce. Fails soft: a provider
/// outage must not fail an order or a sign-in, so callers get a result to act on.
/// </summary>
internal sealed class CommunicationClient(HttpClient http, IOptions<CommunicationServiceSettings> options)
    : ISmsSender, IEmailSender
{
    private static readonly ILogger Logger = Log.ForContext("SourceContext", typeof(CommunicationClient).FullName);
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly CommunicationServiceSettings _settings = options.Value;

    public async Task<MessageResult> SendSmsAsync(IReadOnlyCollection<string> to, string message, CancellationToken ct = default)
    {
        var numbers = to.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).Distinct().ToList();
        if (numbers.Count == 0)
            return new MessageResult(false, 0, 0, "No recipients.");
        if (string.IsNullOrWhiteSpace(_settings.BaseUrl))
            return new MessageResult(false, 0, numbers.Count, "Communication service is not configured.");

        try
        {
            using var response = await http.PostAsJsonAsync("api/sms/send",
                new { to = numbers, message, senderId = _settings.SmsSenderId }, JsonOpts, ct);
            if (!response.IsSuccessStatusCode)
                return new MessageResult(false, 0, numbers.Count, $"SMS service returned {(int)response.StatusCode}.");

            var envelope = await response.Content.ReadFromJsonAsync<Envelope<DeliveryCounts>>(JsonOpts, ct);
            var data = envelope?.Data;
            if (envelope?.Success != true || data is null)
                return new MessageResult(false, 0, numbers.Count, envelope?.Message ?? "SMS service failure.");
            return new MessageResult(data.Failed == 0 && data.Sent > 0, data.Sent, data.Failed);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Logger.Warning(ex, "SMS send failed");
            return new MessageResult(false, 0, numbers.Count, "SMS service is unavailable.");
        }
    }

    public async Task<MessageResult> SendEmailAsync(EmailMessage message, CancellationToken ct = default)
    {
        var recipients = message.To.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (recipients.Count == 0)
            return new MessageResult(false, 0, 0, "No recipients.");
        if (string.IsNullOrWhiteSpace(_settings.BaseUrl))
            return new MessageResult(false, 0, recipients.Count, "Communication service is not configured.");

        try
        {
            using var response = await http.PostAsJsonAsync("api/email/send", new
            {
                to = recipients,
                cc = Array.Empty<string>(),
                bcc = Array.Empty<string>(),
                replyTo = message.ReplyTo,
                subject = message.Subject,
                body = message.Body,
                isHtml = message.IsHtml,
                fromName = message.FromName
            }, JsonOpts, ct);
            if (!response.IsSuccessStatusCode)
                return new MessageResult(false, 0, recipients.Count, $"Email service returned {(int)response.StatusCode}.");

            var envelope = await response.Content.ReadFromJsonAsync<Envelope<DeliveryCounts>>(JsonOpts, ct);
            var data = envelope?.Data;
            if (envelope?.Success != true || data is null)
                return new MessageResult(false, 0, recipients.Count, envelope?.Message ?? "Email service failure.");
            return new MessageResult(data.Failed == 0 && data.Sent == recipients.Count, data.Sent, data.Failed);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Logger.Warning(ex, "Email send failed");
            return new MessageResult(false, 0, recipients.Count, "Email service is unavailable.");
        }
    }

    private sealed class Envelope<T>
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public T? Data { get; set; }
    }

    private sealed class DeliveryCounts
    {
        public int Total { get; set; }
        public int Sent { get; set; }
        public int Failed { get; set; }
    }
}
