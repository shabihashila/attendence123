// Author: Md. Sakibur Rahman
// Date:   2026-05-14
using FegStorage.WebApi.Common;
using FegStorage.WebApi.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Serilog;

namespace FegStorage.WebApi.Endpoints;

// Serves Local-backend bytes for HMAC-signed URLs. Anonymous on purpose
// -- the HMAC IS the auth. The signature proves the caller (typically
// the consumer's API at upload time) intended this exact (client,
// container, key, expires) tuple. Without a matching signature, every
// request is a 403.
//
// The signed payload carries the client folder because that's part of
// the on-disk path ({root}/{client}/{container}/{key}): uploads land in
// a per-client bucket, so the anonymous view path needs the same
// segment to find the bytes. Legacy payloads (pre-per-client-buckets,
// no client segment) still verify; for those we look the file up at the
// root and, failing that, search the known client folders.
//
// Why a dedicated endpoint rather than UseStaticFiles: we need to
// gate on signature + lifetime BEFORE streaming bytes. Static files
// can't do that.
public static class LocalFileViewEndpoints
{
    public static IEndpointRouteBuilder MapLocalFileViewEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/files/view")
            .AllowAnonymous()
            .WithTags("LocalFileView");

        group.MapGet("/{payload}", (
                string payload,
                string? sig,
                LocalUrlSigner urlSigner,
                IOptions<LocalBackendSettings> localOpts,
                IHostEnvironment env,
                HttpResponse response) =>
            {
                if (string.IsNullOrWhiteSpace(payload) || string.IsNullOrWhiteSpace(sig))
                    return Results.BadRequest(ApiResponse.Fail("Missing payload or signature"));

                var verified = urlSigner.Verify(payload, sig);
                if (verified is null)
                {
                    Log.Information("Local view URL rejected (sig mismatch or expired)");
                    return Results.Json(ApiResponse.Fail("Invalid or expired view URL"),
                        statusCode: StatusCodes.Status403Forbidden);
                }

                var (clientFolder, container, key) = verified.Value;
                // Resolve the root the same way the backend factory does:
                // relative paths against ContentRootPath, then the per-
                // client bucket when the payload carries one. Legacy URLs
                // (client == null) fall back to the root, then a search of
                // the client folders under it.
                var root = ResolveRoot(localOpts.Value.BasePath, env);
                if (clientFolder is not null)
                {
                    if (!IsSafePath(clientFolder, container, key))
                        return Results.Json(ApiResponse.Fail("Invalid key segment"),
                            statusCode: StatusCodes.Status403Forbidden);

                    var full = Resolve(root, clientFolder, container, key);
                    if (full is null) return Results.NotFound(ApiResponse.Fail("Object not found"));
                    return Serve(full, key, response);
                }

                // Legacy payload: {container}:{key}:{expires}, file placed
                // either directly under root or inside one of the client
                // buckets created since.
                if (IsSafePath(container, key))
                {
                    var direct = Resolve(root, clientFolder: null, container, key);
                    if (direct is not null) return Serve(direct, key, response);

                    foreach (var dir in Directory.EnumerateDirectories(root))
                    {
                        var candidate = Resolve(dir, clientFolder: null, container, key);
                        if (candidate is not null) return Serve(candidate, key, response);
                    }
                }

                return Results.NotFound(ApiResponse.Fail("Object not found"));
            });

        return routes;
    }

    private static string ResolveRoot(string basePath, IHostEnvironment env)
        => Path.IsPathRooted(basePath)
            ? Path.GetFullPath(basePath)
            : Path.GetFullPath(Path.Combine(env.ContentRootPath, basePath));

    // Normalizes segments, rejects . / .. / empty, and returns the
    // absolute path (or null if it escapes the root or doesn't exist).
    private static string? Resolve(string root, string? clientFolder, string container, string key)
    {
        var relative = (string.IsNullOrEmpty(clientFolder) ? "" : clientFolder + "/")
            + container.Trim('/', '\\') + "/" + key.Trim('/', '\\');
        relative = relative.Replace('\\', '/');
        foreach (var seg in relative.Split('/'))
        {
            if (string.IsNullOrWhiteSpace(seg) || seg == "." || seg == "..") return null;
        }

        var rootFull = Path.GetFullPath(root);
        var full = Path.GetFullPath(Path.Combine(rootFull, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(full, rootFull, StringComparison.OrdinalIgnoreCase))
            return null;

        return File.Exists(full) ? full : null;
    }

    private static bool IsSafePath(params string[] segments)
    {
        foreach (var seg in segments)
        {
            foreach (var part in seg.Trim('/', '\\').Replace('\\', '/').Split('/'))
            {
                if (string.IsNullOrWhiteSpace(part) || part == "." || part == "..") return false;
            }
        }
        return true;
    }

    private static IResult Serve(string full, string key, HttpResponse response)
    {
        var contentType = GuessContentType(key);
        var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        // Public cache up to the URL's expiry -- the signed URL is the
        // cache key, and the URL itself expires.
        response.Headers.CacheControl = "public, max-age=900";
        return Results.File(stream, contentType);
    }

    private static string GuessContentType(string key)
    {
        var ext = Path.GetExtension(key).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png"            => "image/png",
            ".gif"            => "image/gif",
            ".webp"           => "image/webp",
            ".pdf"            => "application/pdf",
            ".csv"            => "text/csv",
            ".txt"            => "text/plain",
            ".json"           => "application/json",
            ".xlsx"           => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".docx"           => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            _                 => "application/octet-stream"
        };
    }
}
