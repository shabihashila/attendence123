// Author: Md. Sakibur Rahman
// Date:   2026-04-29
namespace FegCommunication.WebApi.Email;

// Bound from the "Smtp" section. If `Host` is empty or `Enabled = false`
// EmailService short-circuits with a clear "email not configured"
// result so dev boxes without an MTA don't throw at runtime.
public sealed class SmtpSettings
{
    public bool Enabled { get; set; } = false;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "no-reply@hraccounts.local";
    public string FromName { get; set; } = "HRAccounts Communications";
}
