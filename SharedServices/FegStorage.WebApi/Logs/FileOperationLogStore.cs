// Author: Md. Sakibur Rahman
// Date:   2026-05-13
using Dapper;
using Microsoft.Data.SqlClient;
using Serilog;

namespace FegStorage.WebApi.Logs;

internal sealed class FileOperationLogStore(
    IConfiguration config) : IFileOperationLogStore
{
    private readonly string? _connectionString = config.GetConnectionString("DefaultConnection");

    private const string CreateTableSql = @"
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'FileOperationLog')
BEGIN
    CREATE TABLE dbo.FileOperationLog (
        Id         BIGINT IDENTITY(1,1) PRIMARY KEY,
        Kind       NVARCHAR(20) NOT NULL,
        Provider   NVARCHAR(20) NOT NULL,
        Container  NVARCHAR(120) NOT NULL,
        [Key]      NVARCHAR(500) NOT NULL,
        SizeBytes  BIGINT NULL,
        Status     NVARCHAR(10) NOT NULL,
        Error      NVARCHAR(2000) NULL,
        CallerKey  NVARCHAR(120) NULL,
        AtTime     DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
        CONSTRAINT CK_FileOpLog_Kind     CHECK (Kind     IN (N'Upload', N'Download', N'Delete', N'ViewUrl')),
        CONSTRAINT CK_FileOpLog_Status   CHECK (Status   IN (N'Ok', N'Failed'))
    );
    CREATE INDEX IX_FileOpLog_Kind_AtTime ON dbo.FileOperationLog(Kind, AtTime DESC);
    CREATE INDEX IX_FileOpLog_Container_AtTime ON dbo.FileOperationLog(Container, AtTime DESC);
END";

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        if (!IsConfigured()) { Log.Information("FileOperationLog: no DefaultConnection -- audit disabled"); return; }
        try
        {
            await using var conn = Open();
            await conn.ExecuteAsync(new CommandDefinition(CreateTableSql, cancellationToken: ct));
            Log.Information("FileOperationLog: dbo.FileOperationLog ensured");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "FileOperationLog: failed to ensure schema -- log writes will be skipped");
        }
    }

    public async Task WriteAsync(
        string kind, string provider, string container, string key,
        long? sizeBytes, bool ok, string? error, string? callerKey,
        CancellationToken ct = default)
    {
        if (!IsConfigured()) return;
        try
        {
            await using var conn = Open();
            const string sql = @"
INSERT INTO dbo.FileOperationLog (Kind, Provider, Container, [Key], SizeBytes, Status, Error, CallerKey, AtTime)
VALUES (@Kind, @Provider, @Container, @Key, @SizeBytes, @Status, @Error, @CallerKey, SYSDATETIMEOFFSET());";
            await conn.ExecuteAsync(new CommandDefinition(sql, new
            {
                Kind      = kind,
                Provider  = provider,
                Container = Truncate(container, 120),
                Key       = Truncate(key, 500),
                SizeBytes = sizeBytes,
                Status    = ok ? "Ok" : "Failed",
                Error     = Truncate(error, 2000),
                CallerKey = Truncate(callerKey, 120)
            }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            // Audit failures must never break a real operation.
            Log.Warning(ex, "FileOperationLog: insert failed for {Kind} {Container}/{Key}", kind, container, key);
        }
    }

    public async Task<PagedLogResult> ListAsync(LogFilterQuery q, CancellationToken ct = default)
    {
        var page = q.Page <= 0 ? 1 : q.Page;
        var size = q.PageSize <= 0 ? 50 : q.PageSize;
        if (!IsConfigured())
            return new PagedLogResult { Page = page, PageSize = size };

        var where = new List<string>();
        var p = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(q.Kind))     { where.Add("Kind = @Kind");         p.Add("Kind", q.Kind); }
        if (!string.IsNullOrWhiteSpace(q.Provider)) { where.Add("Provider = @Provider"); p.Add("Provider", q.Provider); }
        if (!string.IsNullOrWhiteSpace(q.Status))   { where.Add("Status = @Status");     p.Add("Status", q.Status); }
        if (q.From is DateTime f)                   { where.Add("AtTime >= @From");      p.Add("From", f); }
        if (q.To   is DateTime t)                   { where.Add("AtTime <= @To");        p.Add("To",   t.AddDays(1).AddTicks(-1)); }
        var clause = where.Count == 0 ? "" : "WHERE " + string.Join(" AND ", where);

        p.Add("Skip", (page - 1) * size);
        p.Add("Take", size);

        var sql = $@"
SELECT COUNT(1) FROM dbo.FileOperationLog {clause};
SELECT Id, Kind, Provider, Container, [Key] AS [Key], SizeBytes, Status, Error, CallerKey, AtTime
FROM dbo.FileOperationLog
{clause}
ORDER BY AtTime DESC, Id DESC
OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;";

        await using var conn = Open();
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(sql, p, cancellationToken: ct));
        var total = await multi.ReadFirstAsync<int>();
        var rows  = (await multi.ReadAsync<FileOperationLogRow>()).ToList();

        return new PagedLogResult { Total = total, Page = page, PageSize = size, Rows = rows };
    }

    private bool IsConfigured() => !string.IsNullOrWhiteSpace(_connectionString);
    private SqlConnection Open() { var c = new SqlConnection(_connectionString); c.Open(); return c; }
    private static string? Truncate(string? s, int n) => string.IsNullOrEmpty(s) ? s : (s.Length <= n ? s : s.Substring(0, n));
}
