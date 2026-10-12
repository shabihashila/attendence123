// Author: Md. Sakibur Rahman
// Date:   2026-05-14
using System.Security.Claims;
using System.Text.Json;
using FegStorage.WebApi.Common;
using FegStorage.WebApi.Logs;
using FegStorage.WebApi.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FegStorage.WebApi.Endpoints;

public static class FilesEndpoints
{
    // 24h max view-URL lifetime regardless of what the caller asks for.
    // Long enough for a print job to finish + the operator to come back
    // from lunch; short enough that a leaked link doesn't grant forever access.
    private static readonly TimeSpan MaxViewUrlTtl = TimeSpan.FromHours(24);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase
    };

    public static IEndpointRouteBuilder MapFilesEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/files")
            .RequireAuthorization()
            .WithTags("Files");

        // Multipart so the file bytes stream straight through to the
        // backend -- no base64 inflation, no double buffering. The JSON
        // metadata travels as a separate form field "meta" alongside the
        // file part "file".
        group.MapPost("/upload", async (
                IFormFile file,
                [FromForm] string meta,
                IStorageBackendFactory factory,
                IFileOperationLogStore audit,
                IOptions<LocalBackendSettings> localOpts,
                IOptions<UploadLimits> uploadOpts,
                ClaimsPrincipal user,
                CancellationToken ct) =>
            {
                if (file is null || file.Length == 0)
                    return Results.BadRequest(ApiResponse.Fail("A non-empty 'file' part is required"));
                var maxBytes = uploadOpts.Value.MaxUploadBytes;
                if (maxBytes > 0 && file.Length > maxBytes)
                    return Results.BadRequest(ApiResponse.Fail($"File exceeds the {maxBytes}-byte limit"));
                if (string.IsNullOrWhiteSpace(meta))
                    return Results.BadRequest(ApiResponse.Fail("A 'meta' part with the JSON upload metadata is required"));

                UploadMetadata? m;
                try { m = JsonSerializer.Deserialize<UploadMetadata>(meta, JsonOpts); }
                catch (JsonException jx) { return Results.BadRequest(ApiResponse.Fail($"Invalid meta JSON: {jx.Message}")); }

                if (m is null || m.Config is null || string.IsNullOrWhiteSpace(m.Key))
                    return Results.BadRequest(ApiResponse.Fail("meta.config and meta.key are required"));

                var backend = factory.Create(m.Config);
                var caller  = user.Identity?.Name;
                StoredObjectInfo info;
                try
                {
                    await using var stream = file.OpenReadStream();
                    info = await backend.UploadAsync(
                        m.Config.Container,
                        m.Key,
                        stream,
                        m.ContentType ?? file.ContentType ?? "application/octet-stream",
                        ct);
                }
                catch (Exception ex)
                {
                    await audit.WriteAsync("Upload", m.Config.Provider.ToString(), m.Config.Container, m.Key,
                        file.Length, ok: false, error: ex.Message, callerKey: caller, ct);
                    throw;
                }

                // Issue a view URL alongside the upload result so callers don't
                // need a follow-up round-trip just to render the new object.
                var ttl = ClampTtl(TimeSpan.FromMinutes(m.ViewUrlMinutes ?? localOpts.Value.DefaultViewUrlMinutes));
                var view = await backend.GetViewUrlAsync(m.Config.Container, m.Key, ttl, ct);

                await audit.WriteAsync("Upload", m.Config.Provider.ToString(), m.Config.Container, m.Key,
                    info.SizeBytes, ok: true, error: null, callerKey: caller, ct);

                return Results.Ok(ApiResponse<UploadResult>.Ok(new UploadResult
                {
                    Provider          = m.Config.Provider,
                    Container         = m.Config.Container,
                    Key               = m.Key,
                    SizeBytes         = info.SizeBytes,
                    ContentType       = info.ContentType,
                    Sha256            = info.Sha256,
                    ViewUrl           = view.ViewUrl,
                    ViewUrlExpiresAt  = view.ExpiresAt
                }, $"Uploaded {info.SizeBytes} bytes to {m.Config.Provider}:{m.Config.Container}/{m.Key}"));
            })
            .DisableAntiforgery()
            .Accepts<UploadFormModel>("multipart/form-data")
            .Produces<ApiResponse<UploadResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse>(StatusCodes.Status400BadRequest);

        // Streams the bytes back as application/octet-stream. POST (not GET)
        // because the JSON body carries provider creds -- those don't belong
        // in a URL.
        group.MapPost("/download", async (
                FileOperationRequest req,
                IStorageBackendFactory factory,
                IFileOperationLogStore audit,
                ClaimsPrincipal user,
                CancellationToken ct) =>
            {
                if (req?.Config is null || string.IsNullOrWhiteSpace(req.Key))
                    return Results.BadRequest(ApiResponse.Fail("config and key are required"));

                var backend = factory.Create(req.Config);
                var caller  = user.Identity?.Name;

                DownloadedObject obj;
                try
                {
                    obj = await backend.DownloadAsync(req.Config.Container, req.Key, ct);
                }
                catch (Exception ex)
                {
                    await audit.WriteAsync("Download", req.Config.Provider.ToString(), req.Config.Container, req.Key,
                        null, ok: false, error: ex.Message, callerKey: caller, ct);
                    throw;
                }

                await audit.WriteAsync("Download", req.Config.Provider.ToString(), req.Config.Container, req.Key,
                    obj.SizeBytes, ok: true, error: null, callerKey: caller, ct);

                // Results.File takes ownership of the stream and disposes
                // it after the response writes -- safe to skip the using here.
                return Results.File(obj.Content, obj.ContentType);
            })
            .ValidateBody<FileOperationRequest>()
            .Produces(StatusCodes.Status200OK)
            .Produces<ApiResponse>(StatusCodes.Status400BadRequest);

        group.MapPost("/delete", async (
                FileOperationRequest req,
                IStorageBackendFactory factory,
                IFileOperationLogStore audit,
                ClaimsPrincipal user,
                CancellationToken ct) =>
            {
                if (req?.Config is null || string.IsNullOrWhiteSpace(req.Key))
                    return Results.BadRequest(ApiResponse.Fail("config and key are required"));

                var backend = factory.Create(req.Config);
                var caller  = user.Identity?.Name;
                bool existed;
                try
                {
                    existed = await backend.DeleteAsync(req.Config.Container, req.Key, ct);
                }
                catch (Exception ex)
                {
                    await audit.WriteAsync("Delete", req.Config.Provider.ToString(), req.Config.Container, req.Key,
                        null, ok: false, error: ex.Message, callerKey: caller, ct);
                    throw;
                }

                await audit.WriteAsync("Delete", req.Config.Provider.ToString(), req.Config.Container, req.Key,
                    null, ok: true, error: null, callerKey: caller, ct);

                var msg = existed ? "Deleted" : "Already absent (idempotent)";
                return Results.Ok(ApiResponse<DeleteResult>.Ok(new DeleteResult { Existed = existed }, msg));
            })
            .ValidateBody<FileOperationRequest>()
            .Produces<ApiResponse<DeleteResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse>(StatusCodes.Status400BadRequest);

        // Re-issue a view URL for an existing object. The previous URL may
        // have expired, or the caller may want a longer-lived one.
        group.MapPost("/view-url", async (
                ViewUrlRequest req,
                IStorageBackendFactory factory,
                IFileOperationLogStore audit,
                IOptions<LocalBackendSettings> localOpts,
                ClaimsPrincipal user,
                CancellationToken ct) =>
            {
                if (req?.Config is null || string.IsNullOrWhiteSpace(req.Key))
                    return Results.BadRequest(ApiResponse.Fail("config and key are required"));

                var backend = factory.Create(req.Config);
                var caller  = user.Identity?.Name;
                var ttl = ClampTtl(TimeSpan.FromMinutes(req.ViewUrlMinutes ?? localOpts.Value.DefaultViewUrlMinutes));
                var view = await backend.GetViewUrlAsync(req.Config.Container, req.Key, ttl, ct);

                await audit.WriteAsync("ViewUrl", req.Config.Provider.ToString(), req.Config.Container, req.Key,
                    null, ok: true, error: null, callerKey: caller, ct);

                return Results.Ok(ApiResponse<ViewUrlResult>.Ok(view, $"View URL valid until {view.ExpiresAt:o}"));
            })
            .ValidateBody<ViewUrlRequest>()
            .Produces<ApiResponse<ViewUrlResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse>(StatusCodes.Status400BadRequest);

        return routes;
    }

    private static TimeSpan ClampTtl(TimeSpan ttl)
    {
        if (ttl <= TimeSpan.Zero) return TimeSpan.FromMinutes(5);
        return ttl > MaxViewUrlTtl ? MaxViewUrlTtl : ttl;
    }

    // Swagger schema hint for the multipart upload endpoint. Mirrors the
    // form fields the handler actually reads ("file" + "meta").
    private sealed class UploadFormModel
    {
        public IFormFile File { get; set; } = default!;
        public string Meta { get; set; } = string.Empty;
    }
}
