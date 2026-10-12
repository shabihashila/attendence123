// Author: Md. Sakibur Rahman
// Date:   2026-05-13
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace FegStorage.WebApi.Auth;

// AuthenticationHandler for the "ApiKey" scheme. Reads the configured
// header (default X-Api-Key) and validates against ApiKeySettings.Value.
// On match issues a single ClaimsIdentity carrying a "service" name
// claim so [Authorize] passes downstream.
//
// We deliberately use AuthenticationHandler (not custom middleware) so
// the handler integrates with [Authorize] / AllowAnonymous exactly the
// way HRAccounts's JWT does -- the controllers don't have to know how the
// caller proved themselves. Mirrored verbatim from CommunicationService.
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<ApiKeySettings> apiKeyOpts)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    private readonly ApiKeySettings _apiKey = apiKeyOpts.Value;

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var headerName = string.IsNullOrWhiteSpace(_apiKey.Header) ? "X-Api-Key" : _apiKey.Header;
        if (!Request.Headers.TryGetValue(headerName, out var presented) || presented.Count == 0)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var supplied = presented.ToString();

        // 1) Per-client map wins. Match stamps the principal Name with
        //    the matched client name, which the StorageBackendFactory
        //    uses to bucket uploads as data/{ClientName}/... .
        if (_apiKey.Clients is { Count: > 0 })
        {
            foreach (var kv in _apiKey.Clients)
            {
                if (!string.IsNullOrEmpty(kv.Value) && FixedTimeEquals(kv.Value, supplied))
                {
                    return Task.FromResult(Success(kv.Key));
                }
            }
        }

        // 2) Legacy single-key fallback. Lands in data/default/... .
        if (!string.IsNullOrEmpty(_apiKey.Value) && FixedTimeEquals(_apiKey.Value, supplied))
        {
            return Task.FromResult(Success("default"));
        }

        Logger.LogWarning("ApiKey auth failed for {Path}", Request.Path);
        return Task.FromResult(AuthenticateResult.Fail("Invalid API key"));
    }

    private AuthenticateResult Success(string clientName)
    {
        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Name, clientName) },
            SchemeName);
        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var diff = 0;
        for (var i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }
}
