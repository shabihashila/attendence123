// Author: Md. Sakibur Rahman
// Date:   2026-04-29
namespace FegCommunication.WebApi.Sms;

// Service-wide SMS config bound from "Sms". Per-provider credentials
// live under "Sms:Providers:<Name>" (see AlphaSmsSettings). The
// per-request SendSmsRequest.Provider overrides DefaultProvider when set.
public sealed class SmsSettings
{
    public string DefaultProvider { get; set; } = "Log";
    public string SenderId        { get; set; } = "HRAccounts";
}
