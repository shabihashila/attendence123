// Author: Md. Sakibur Rahman
// Date:   2026-04-29
namespace FegCommunication.WebApi.Auth;

// Bound from the "ApiKey" config section. Header is the request-header
// name to look in; Value is the legacy single shared secret.
//
// Clients maps a caller name -> that caller's own key:
// { "HomeoCare": "...", "HRAccounts": "..." }. The matched name becomes the
// authenticated principal's name, so per-app keys can be rotated (or revoked)
// independently and the audit log can tell callers apart.
//
// Value still works on its own and authenticates as "comm-client", so
// existing consumers keep running unchanged; issue per-app keys when you
// want the isolation (and rotate them independently).
public sealed class ApiKeySettings
{
    public string Header { get; set; } = "X-Api-Key";
    public string Value  { get; set; } = string.Empty;

    public Dictionary<string, string> Clients { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
