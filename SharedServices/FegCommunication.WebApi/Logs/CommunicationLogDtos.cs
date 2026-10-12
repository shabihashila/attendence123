// Author: Md. Sakibur Rahman
// Date:   2026-04-29
namespace FegCommunication.WebApi.Logs;

public sealed class LogFilterQuery
{
    public string? Kind   { get; set; }     // Email | Sms
    public string? Status { get; set; }     // Sent | Failed
    public DateTime? From { get; set; }
    public DateTime? To   { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public sealed class CommunicationLogRow
{
    public long   Id          { get; set; }
    public string Kind        { get; set; } = string.Empty;     // Email | Sms
    public string Recipient   { get; set; } = string.Empty;
    public string? Subject    { get; set; }                     // Email only
    public string? Sender     { get; set; }                     // From-address (email) or SenderId (sms)
    public string  Status     { get; set; } = "Sent";           // Sent | Failed
    public string? Error      { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? Provider   { get; set; }                     // ISmsProvider.Name (Alpha, Log, ...); null for Email rows
    public string? CallerKey  { get; set; }                     // ClaimsPrincipal name set by ApiKey handler
    public DateTimeOffset SentAt { get; set; }
}

public sealed class PagedLogResult
{
    public int Total { get; set; }
    public int Page  { get; set; }
    public int PageSize { get; set; }
    public List<CommunicationLogRow> Rows { get; set; } = new();
}
