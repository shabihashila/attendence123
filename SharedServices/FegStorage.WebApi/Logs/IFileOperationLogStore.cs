// Author: Md. Sakibur Rahman
// Date:   2026-05-13
namespace FegStorage.WebApi.Logs;

// Tiny audit store -- one row per operation. Backed by
// dbo.FileOperationLog in the same SQL Server instance the service is
// configured against. Mirrors CommunicationService's audit pattern so
// operators have one mental model for both services.
public interface IFileOperationLogStore
{
    Task EnsureSchemaAsync(CancellationToken ct = default);

    Task WriteAsync(
        string kind,
        string provider,
        string container,
        string key,
        long? sizeBytes,
        bool ok,
        string? error,
        string? callerKey,
        CancellationToken ct = default);

    Task<PagedLogResult> ListAsync(LogFilterQuery q, CancellationToken ct = default);
}
