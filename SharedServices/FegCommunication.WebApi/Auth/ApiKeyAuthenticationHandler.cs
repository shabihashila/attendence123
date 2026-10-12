// Author: Md. Sakibur Rahman
// Date:   2026-04-29
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace FegCommunication.WebApi.Auth;

// AuthenticationHandler for the "ApiKey" scheme. Reads the configured
// header (default X-Api-Key) and validates against ApiKeySettings.Value.
// On match, issues a single ClaimsIdentity carrying a "service" name
// claim so [Authorize] passes downstream.
//
// We deliberately use AuthenticationHandler (not custom middleware) so
// the handler integrates with [Authorize] / AllowAnonymous exactly the
// way HRAccounts's JWT does -- the controllers don't have to know how the
// caller proved themselves.
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
            // Don't fail outright -- let [Authorize] convert "no auth"
            // into a 401 via HandleChallengeAsync. Returning NoResult
            // also lets AllowAnonymous endpoints work.
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var supplied = presented.ToString();
        var caller = ResolveCaller(supplied);
        if (caller is null)
        {
            Logger.LogWarning("ApiKey auth failed for {Path}", Request.Path);
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key"));
        }

        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Name, caller) },
            SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket    = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    // Returns the caller name the presented key belongs to, or null when it
    // matches nothing. Per-app keys (ApiKey:Clients) win over the legacy
    // shared ApiKey:Value, which authenticates as the generic "comm-client".
    // Every candidate is compared even after a hit so the work stays
    // independent of which key matched.
    public const string LegacyCallerName = "comm-client";

    private string? ResolveCaller(string supplied)
    {
        string? hit = null;

        foreach (var (name, key) in _apiKey.Clients)
        {
            if (!string.IsNullOrEmpty(key) && FixedTimeEquals(key, supplied))
                hit ??= name;
        }

        if (!string.IsNullOrEmpty(_apiKey.Value) && FixedTimeEquals(_apiKey.Value, supplied))
            hit ??= LegacyCallerName;

        return hit;
    }

    // Constant-time compare to avoid timing-attack hints. The keys are
    // short enough that this is overkill, but it costs nothing and
    // captures the right intent.
    private static bool FixedTimeEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var diff = 0;
        for (var i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }
}
