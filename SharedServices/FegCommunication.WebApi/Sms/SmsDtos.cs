// Author: Md. Sakibur Rahman
// Date:   2026-04-29
using System.ComponentModel.DataAnnotations;

namespace FegCommunication.WebApi.Sms;

public sealed class SendSmsRequest
{
    [Required, MinLength(1)] public List<string> To { get; set; } = new();

    [Required, MaxLength(1000)] public string Message { get; set; } = string.Empty;

    // Optional override of the configured SenderId -- some providers
    // bill differently per-sender, so HR may want to pick "HRAccounts" for
    // notices and a different brand for marketing.
    [MaxLength(40)] public string? SenderId { get; set; }

    // Picks the SMS gateway: "Log" (dev no-op), "Alpha" (sms.net.bd).
    // Case-insensitive. Falls back to Sms:DefaultProvider when omitted.
    [MaxLength(40)] public string? Provider { get; set; }

    // Bangladesh regulators require a pre-approved content_id on non-OTP
    // commercial bulk SMS. AlphaSmsProvider forwards it as the gateway's
    // `content_id` query param when set; LogSmsProvider just echoes it.
    [MaxLength(64)] public string? ContentId { get; set; }

    // Per-tenant provider config carried inline. When set, the picked
    // provider uses these credentials instead of comm's IOptions fallback.
    // Mirrors the FileStorage BackendConfig pattern: each calling app
    // (HomeoCare / SalesSaas / HRAccounts) stores its tenants' credentials
    // in its own DB (encrypted) and forwards them per call, so comm holds
    // no per-tenant state. Null = fall back to comm's appsettings IOptions
    // (the back-compat path HomeoCare uses today).
    public SmsBackendConfig? Config { get; set; }
}

// Polymorphic provider config. Only the sub-DTO whose name matches the
// request's Provider field is read; the others are ignored. Strongly typed
// so Swagger surfaces the right fields per gateway.
public sealed class SmsBackendConfig
{
    public AlphaSmsConfig? Alpha { get; set; }
    // Future providers (Twilio, Vonage, MessageBird, ...) get their own
    // sub-DTO here. Add the property, add the provider impl + DI line, done.
}

public sealed class AlphaSmsConfig
{
    // Per-tenant sms.net.bd API key. Required when SendSmsRequest.Config is
    // supplied -- if you set Config you're opting out of the IOptions fallback.
    [Required, MaxLength(240)] public string ApiKey { get; set; } = string.Empty;

    // Optional override of the gateway base URL (e.g. a regional endpoint).
    // Null = use the AlphaSmsSettings:BaseUrl that comm was started with.
    [MaxLength(240)] public string? BaseUrl { get; set; }
}

public sealed class SmsDeliveryRow
{
    public string To    { get; set; } = string.Empty;
    public bool   Sent  { get; set; }
    public string? Error { get; set; }
    public string? ProviderMessageId { get; set; }
}

public sealed class SendSmsResult
{
    public int Total  { get; set; }
    public int Sent   { get; set; }
    public int Failed { get; set; }
    public string Provider { get; set; } = string.Empty;
    public List<SmsDeliveryRow> Recipients { get; set; } = new();
}
