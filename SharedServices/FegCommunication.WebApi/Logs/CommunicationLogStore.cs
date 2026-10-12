// Author: Md. Sakibur Rahman
// Date:   2026-04-29
using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace FegCommunication.WebApi.Logs;

internal sealed class CommunicationLogStore(
    IConfiguration config) : ICommunicationLogStore
{
    private readonly string? _connectionString = config.GetConnectionString("DefaultConnection");

    // The ALTER block is for DBs created before Provider was added -- it's a
    // no-op once the column exists. Kept inline (rather than as a separate
    // migration script) so the comm service stays self-bootstrapping.
    private const string CreateTableSql = @"
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'CommunicationLog')
BEGIN
    CREATE TABLE dbo.CommunicationLog (
        Id                BIGINT IDENTITY(1,1) PRIMARY KEY,
        Kind              NVARCHAR(10) NOT NULL,
        Recipient         NVARCHAR(300) NOT NULL,
        Subject           NVARCHAR(300) NULL,
        Sender            NVARCHAR(120) NULL,
        Status            NVARCHAR(10) NOT NULL,
        Error             NVARCHAR(2000) NULL,
        ProviderMessageId NVARCHAR(120) NULL,
        Provider          NVARCHAR(40)  NULL,
        CallerKey         NVARCHAR(120) NULL,
        SentAt            DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
        CONSTRAINT CK_CommLog_Kind   CHECK (Kind   IN (N'Email', N'Sms')),
        CONSTRAINT CK_CommLog_Status CHECK (Status IN (N'Sent',  N'Failed'))
    );
    CREATE INDEX IX_CommLog_Kind_SentAt ON dbo.CommunicationLog(Kind, SentAt DESC);
END
ELSE IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.CommunicationLog') AND name = 'Provider')
BEGIN
    ALTER TABLE dbo.CommunicationLog ADD Provider NVARCHAR(40) NULL;
END";

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        if (!IsConfigured()) { Log.Information("CommunicationLog: no DefaultConnection -- audit log disabled"); return; }
        try
        {
            await using var conn = Open();
            await conn.ExecuteAsync(new CommandDefinition(CreateTableSql, cancellationToken: ct));
            Log.Information("CommunicationLog: dbo.CommunicationLog ensured");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "CommunicationLog: failed to ensure schema -- log writes will be skipped");
        }
    }

    public Task LogEmailAsync(
        string toAddress, string? subject, string? from,
        bool sent, string? error,
        string? callerKey, CancellationToken ct = default)
        => InsertAsync("Email", toAddress, subject, from, sent, error, providerMessageId: null, provider: null, callerKey, ct);

    public Task LogSmsAsync(
        string toNumber, string? sender,
        bool sent, string? error, string? providerMessageId,
        string? provider, string? callerKey, CancellationToken ct = default)
        => InsertAsync("Sms", toNumber, subject: null, sender, sent, error, providerMessageId, provider, callerKey, ct);

    private async Task InsertAsync(
        string kind, string recipient, string? subject, string? sender,
        bool sent, string? error, string? providerMessageId, string? provider, string? callerKey,
        CancellationToken ct)
    {
        if (!IsConfigured()) return;
        try
        {
            await using var conn = Open();
            const string sql = @"
INSERT INTO dbo.CommunicationLog
    (Kind, Recipient, Subject, Sender, Status, Error, ProviderMessageId, Provider, CallerKey, SentAt)
VALUES
    (@Kind, @Recipient, @Subject, @Sender, @Status, @Error, @ProviderMessageId, @Provider, @CallerKey, SYSDATETIMEOFFSET());";
            await conn.ExecuteAsync(new CommandDefinition(sql, new
            {
                Kind   = kind,
                Recipient = Truncate(recipient, 300),
                Subject = Truncate(subject, 300),
                Sender  = Truncate(sender, 120),
                Status  = sent ? "Sent" : "Failed",
                Error   = Truncate(error, 2000),
                ProviderMessageId = Truncate(providerMessageId, 120),
                Provider = Truncate(provider, 40),
                CallerKey = Truncate(callerKey, 120)
            }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            // Audit failures must never break a real send. Just log and move on.
            Log.Warning(ex, "CommunicationLog: insert failed for {Kind} {Recipient}", kind, recipient);
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
        if (!string.IsNullOrWhiteSpace(q.Kind))   { where.Add("Kind = @Kind");   p.Add("Kind", q.Kind); }
        if (!string.IsNullOrWhiteSpace(q.Status)) { where.Add("Status = @Status"); p.Add("Status", q.Status); }
        if (q.From is DateTime f)                 { where.Add("SentAt >= @From"); p.Add("From", f); }
        if (q.To   is DateTime t)                 { where.Add("SentAt <= @To");   p.Add("To",   t.AddDays(1).AddTicks(-1)); }
        var clause = where.Count == 0 ? "" : "WHERE " + string.Join(" AND ", where);

        p.Add("Skip", (page - 1) * size);
        p.Add("Take", size);

        var sql = $@"
SELECT COUNT(1) FROM dbo.CommunicationLog {clause};
SELECT Id, Kind, Recipient, Subject, Sender, Status, Error, ProviderMessageId, Provider, CallerKey, SentAt
FROM dbo.CommunicationLog
{clause}
ORDER BY SentAt DESC, Id DESC
OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;";

        await using var conn = Open();
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(sql, p, cancellationToken: ct));
        var total = await multi.ReadFirstAsync<int>();
        var rows  = (await multi.ReadAsync<CommunicationLogRow>()).ToList();

        return new PagedLogResult { Total = total, Page = page, PageSize = size, Rows = rows };
    }

    private bool IsConfigured() => !string.IsNullOrWhiteSpace(_connectionString);

    private SqlConnection Open()
    {
        var c = new SqlConnection(_connectionString);
        c.Open();
        return c;
    }

    private static string? Truncate(string? s, int n)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return s.Length <= n ? s : s.Substring(0, n);
    }
}
