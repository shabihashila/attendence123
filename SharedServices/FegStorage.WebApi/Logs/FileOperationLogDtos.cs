// Author: Md. Sakibur Rahman
// Date:   2026-05-13
namespace FegStorage.WebApi.Logs;

public sealed class LogFilterQuery
{
    public string? Kind     { get; set; }   // Upload | Download | Delete | ViewUrl
    public string? Provider { get; set; }   // Local | S3 | R2 | AzureBlob
    public string? Status   { get; set; }   // Ok | Failed
    public DateTime? From   { get; set; }
    public DateTime? To     { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public sealed class FileOperationLogRow
{
    public long   Id        { get; set; }
    public string Kind      { get; set; } = string.Empty;
    public string Provider  { get; set; } = string.Empty;
    public string Container { get; set; } = string.Empty;
    public string Key       { get; set; } = string.Empty;
    public long?  SizeBytes { get; set; }
    public string Status    { get; set; } = "Ok";
    public string? Error    { get; set; }
    public string? CallerKey { get; set; }
    public DateTimeOffset AtTime { get; set; }
}

public sealed class PagedLogResult
{
    public int Total { get; set; }
    public int Page  { get; set; }
    public int PageSize { get; set; }
    public List<FileOperationLogRow> Rows { get; set; } = new();
}
