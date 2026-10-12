// Author: Md. Sakibur Rahman
// Date:   2026-05-13
namespace FegStorage.WebApi.Auth;

// Bound from the "ApiKey" config section.
//   Header  - the request-header name to look in (default: X-Api-Key).
//   Value   - legacy single-key. Matches → principal Name = "default".
//             Files land under data/default/{container}/{key}.
//   Clients - per-consumer keys (e.g. "HRAccounts": "sk_live_..."). A
//             match here is preferred over `Value` and stamps the
//             principal Name with the matched key, which the
//             StorageBackendFactory uses to bucket local uploads as
//             data/{Client}/{container}/{key}. The audit row's
//             CallerKey column inherits the same name automatically.
//
// Each consumer (HRAccounts, HomeoCare, future-app) gets its own key so
// keys rotate independently and a leaked key only impacts one bucket.
public sealed class ApiKeySettings
{
    public string Header { get; set; } = "X-Api-Key";
    public string Value  { get; set; } = string.Empty;
    public Dictionary<string, string> Clients { get; set; } = new();
}
