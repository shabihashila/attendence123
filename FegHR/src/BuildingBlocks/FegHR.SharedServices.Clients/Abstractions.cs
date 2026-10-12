namespace FegHR.SharedServices.Clients;

/// <summary>
/// Tenant-scoped file storage over <c>SharedServices/FegStorage.WebApi</c>.
/// Keys are relative to the tenant: the client prefixes <c>tenants/{tenantId}/</c>,
/// so a module can never read or overwrite another tenant's files by key.
/// </summary>
public interface IFileStorage
{
    Task<StoredFile> UploadAsync(string relativeKey, Stream content, string contentType, CancellationToken ct = default);
    Task<bool> DeleteAsync(string storedKey, CancellationToken ct = default);
    Task<FileViewUrl?> GetViewUrlAsync(string storedKey, int minutes = 30, CancellationToken ct = default);
}

/// <summary>
/// The upload outcome. <see cref="Key"/> is the full tenant-prefixed key to persist;
/// <see cref="Error"/> is set when <see cref="Success"/> is false.
/// </summary>
public sealed record StoredFile(bool Success, string Key, long SizeBytes, string ContentType,
    string? ViewUrl, DateTimeOffset? ViewUrlExpiresAt, string? Error);

public sealed record FileViewUrl(string Url, DateTimeOffset ExpiresAt);

/// <summary>SMS through <c>SharedServices/FegCommunication.WebApi</c>. Never throws.</summary>
public interface ISmsSender
{
    Task<MessageResult> SendSmsAsync(IReadOnlyCollection<string> to, string message, CancellationToken ct = default);
}

/// <summary>Email through <c>SharedServices/FegCommunication.WebApi</c>. Never throws.</summary>
public interface IEmailSender
{
    Task<MessageResult> SendEmailAsync(EmailMessage message, CancellationToken ct = default);
}

public sealed record EmailMessage(
    IReadOnlyCollection<string> To,
    string Subject,
    string Body,
    bool IsHtml = true,
    string? ReplyTo = null,
    string? FromName = null);

public sealed record MessageResult(bool Success, int Sent, int Failed, string? Error = null);
