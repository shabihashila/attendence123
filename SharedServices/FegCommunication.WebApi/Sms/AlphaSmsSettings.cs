// Author: Md. Sakibur Rahman
// Date:   2026-05-14
namespace FegCommunication.WebApi.Sms;

// Per-provider config bound from "Sms:Providers:Alpha". BaseUrl is the
// gateway root (e.g. https://api.sms.net.bd); ApiKey is the account
// token. Stored as plain text in appsettings — prefer environment
// variables or User Secrets in production so the key never lands in
// source control.
public sealed class AlphaSmsSettings
{
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey  { get; set; } = string.Empty;
}
