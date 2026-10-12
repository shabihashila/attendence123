// Author: Md. Sakibur Rahman
// Date:   2026-05-13
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace FegStorage.WebApi.Storage;

// HMAC-SHA256 signer for local-backend view URLs. We sign a payload of
// (client, container, key, expiresAtUnix) and embed it + the signature in
// the URL the UI uses to render `<img src="...">`. That gives the same
// "presigned URL" semantics cloud backends have natively, without
// requiring the bytes to flow back through the consumer API.
//
// The client folder is part of the payload because uploads land under
// {LocalBackend:BasePath}/{client}/{container}/{key} — the per-client
// bucket — and the (anonymous) view endpoint must resolve to the exact
// same path to serve the bytes.
//
// Layout:
//   {ViewBaseUrl}/files/view/{base64url(payload)}?sig={base64url(sig)}
//
// On hit, LocalFileViewController re-computes the HMAC and serves the
// bytes only if the signature matches AND the expiresAt is in the
// future. Idempotent + side-effect-free, safe to put behind any cache.
public sealed class LocalUrlSigner
{
    private readonly byte[] _keyBytes;
    private readonly string _viewBaseUrl;

    public LocalUrlSigner(IOptions<LocalBackendSettings> opts)
    {
        var s = opts.Value;
        if (string.IsNullOrWhiteSpace(s.SigningKey) || s.SigningKey.Length < 32)
            throw new InvalidOperationException(
                "LocalBackend:SigningKey must be at least 32 characters. Rotate via appsettings.");
        _keyBytes = Encoding.UTF8.GetBytes(s.SigningKey);
        _viewBaseUrl = (s.ViewBaseUrl ?? string.Empty).TrimEnd('/');
    }

    public (string Url, DateTimeOffset ExpiresAt) Sign(string clientFolder, string container, string key, TimeSpan ttl)
    {
        var expires = DateTimeOffset.UtcNow.Add(ttl);
        // Payload is intentionally colon-separated fields, not JSON —
        // shorter on the wire, no escape rules to get wrong.
        var payload = $"{clientFolder}:{container}:{key}:{expires.ToUnixTimeSeconds()}";
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var sig = HMACSHA256.HashData(_keyBytes, payloadBytes);

        var payloadEncoded = Base64UrlEncode(payloadBytes);
        var sigEncoded     = Base64UrlEncode(sig);
        var url = $"{_viewBaseUrl}/files/view/{payloadEncoded}?sig={sigEncoded}";
        return (url, expires);
    }

    // Verifies the signature + lifetime. Returns the (client, container,
    // key) the caller can use to look up the bytes on disk, or null if
    // any check fails. Constant-time signature compare to avoid timing
    // oracles. Legacy 3-field payloads (pre-per-client-buckets) verify
    // too — client comes back null and the view endpoint looks the file
    // up without a client segment (plus a client-folder search).
    public (string? ClientFolder, string Container, string Key)? Verify(string payloadEncoded, string sigEncoded)
    {
        byte[] payloadBytes;
        byte[] sigBytes;
        try
        {
            payloadBytes = Base64UrlDecode(payloadEncoded);
            sigBytes     = Base64UrlDecode(sigEncoded);
        }
        catch { return null; }

        var expected = HMACSHA256.HashData(_keyBytes, payloadBytes);
        if (!CryptographicOperations.FixedTimeEquals(expected, sigBytes)) return null;

        var payload = Encoding.UTF8.GetString(payloadBytes);

        // client and container never contain colons; key MAY contain
        // colons, so we slice key:expires off the last colon and then
        // disambiguate 3-field (legacy: container:key:exp) vs 4-field
        // (current: client:container:key:exp) by the third colon.
        var c1 = payload.IndexOf(':');
        if (c1 <= 0) return null;
        var c2 = payload.IndexOf(':', c1 + 1);
        if (c2 <= c1 + 1) return null;
        var c3 = payload.IndexOf(':', c2 + 1);

        string? client;
        string container;
        string keyAndExp;
        if (c3 < 0)
        {
            client = null;
            container = payload[..c1];
            keyAndExp = payload[(c1 + 1)..];
        }
        else
        {
            client = payload[..c1];
            container = payload[(c1 + 1)..c2];
            keyAndExp = payload[(c2 + 1)..];
        }

        var lastColon = keyAndExp.LastIndexOf(':');
        if (lastColon < 0) return null;

        var expPart = keyAndExp[(lastColon + 1)..];
        if (!long.TryParse(expPart, out var expUnix)) return null;
        if (DateTimeOffset.FromUnixTimeSeconds(expUnix) <= DateTimeOffset.UtcNow) return null;

        return (client, container, keyAndExp[..lastColon]);
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string s)
    {
        var padded = s.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "=";  break;
        }
        return Convert.FromBase64String(padded);
    }
}
