// Author: Md. Sakibur Rahman
// Date:   2026-04-29
namespace FegCommunication.WebApi.Logs;

// Tiny audit store -- one row per (kind, recipient, attempt). Backed by
// dbo.CommunicationLog in the same SQL Server instance the service is
// configured against. Keeps the comm service self-contained: no
// dependency on HRAccounts's DbContext.
public interface ICommunicationLogStore
{
    Task EnsureSchemaAsync(CancellationToken ct = default);

    Task LogEmailAsync(
        string toAddress, string? subject, string? from,
        bool sent, string? error,
        string? callerKey, CancellationToken ct = default);

    Task LogSmsAsync(
        string toNumber, string? sender,
        bool sent, string? error, string? providerMessageId,
        string? provider, string? callerKey, CancellationToken ct = default);

    Task<PagedLogResult> ListAsync(LogFilterQuery q, CancellationToken ct = default);
}
