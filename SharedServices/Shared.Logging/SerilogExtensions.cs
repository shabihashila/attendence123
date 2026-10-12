using System.Collections.ObjectModel;
using System.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.MSSqlServer;

namespace Shared.Logging;

/// <summary>
/// Which service is wiring logging and where its rows go.
/// </summary>
public sealed class SerilogLoggingOptions
{
    /// <summary>Types under this namespace are "our code" and reach the DB sink.</summary>
    public string AppNamespacePrefix { get; set; } = string.Empty;

    /// <summary>Stamped on every event as the Service property.</summary>
    public string ServiceName { get; set; } = string.Empty;

    public string TableName { get; set; } = "Logs";
    public string SchemaName { get; set; } = "logs";

    /// <summary>Disable when the versioned database scripts own the log table.</summary>
    public bool AutoCreateSqlTable { get; set; } = true;

    /// <summary>Persist UTC log timestamps. False preserves existing consumers' timestamp behavior.</summary>
    public bool UseUtcTimestamp { get; set; }

    /// <summary>Structured log properties mapped to dedicated table columns.</summary>
    public Collection<SqlColumn> AdditionalColumns { get; set; } = new();
}

/// <summary>
/// Shared Serilog bootstrap, ported from SalesSaas.WebApi.Extensions.SerilogExtensions
/// so every service logs the same way: console sees everything for local
/// debugging; the SQL sink persists only our own code's events, with the
/// rarely-read MessageTemplate/Properties payload columns stripped to keep
/// the table lean. The connection string comes from the caller (config,
/// environment, vault — their choice); null or blank disables DB writes.
/// </summary>
public static class SerilogExtensions
{
    public static WebApplicationBuilder AddSerilogLogging(
        this WebApplicationBuilder builder,
        string? connectionString,
        string? schema,
        Action<SerilogLoggingOptions>? configure = null)
    {
        var options = new SerilogLoggingOptions();
        configure?.Invoke(options);
        if (!string.IsNullOrWhiteSpace(schema))
            options.SchemaName = schema;

        // Keep only the essential columns in the logs table. The rest
        // (MessageTemplate, Properties XML, LogEvent JSON) just bulk up the
        // table and are rarely read.
        var columnOptions = new ColumnOptions();
        columnOptions.TimeStamp.ConvertToUtc = options.UseUtcTimestamp;
        columnOptions.Store.Remove(StandardColumn.MessageTemplate);
        columnOptions.Store.Remove(StandardColumn.Properties);
        columnOptions.AdditionalColumns = options.AdditionalColumns;

        var loggerCfg = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .ReadFrom.Configuration(builder.Configuration)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", options.ServiceName)
            .WriteTo.Console();

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            loggerCfg = loggerCfg.WriteTo.Logger(lc => lc
                .Filter.ByIncludingOnly(e => IsFromAppCode(e, options.AppNamespacePrefix))
                .WriteTo.MSSqlServer(
                    connectionString: connectionString,
                    sinkOptions: new MSSqlServerSinkOptions
                    {
                        TableName = options.TableName,
                        SchemaName = options.SchemaName,
                        AutoCreateSqlTable = options.AutoCreateSqlTable,
                        BatchPostingLimit = 50,
                        BatchPeriod = TimeSpan.FromSeconds(5)
                    },
                    columnOptions: columnOptions));
        }

        Log.Logger = loggerCfg.CreateLogger();
        builder.Host.UseSerilog();
        return builder;
    }

    // Keep an event only if it has no SourceContext (direct Log.Xxx on the
    // static logger) or its SourceContext starts with our app namespace
    // (ILogger<T>). Framework emissions always carry a SourceContext like
    // "Microsoft.*" and are dropped here.
    private static bool IsFromAppCode(LogEvent e, string prefix)
    {
        if (!e.Properties.TryGetValue("SourceContext", out var prop))
            return true;
        if (prop is not ScalarValue { Value: string sc } || string.IsNullOrEmpty(sc))
            return true;
        return string.IsNullOrEmpty(prefix) || sc.StartsWith(prefix, StringComparison.Ordinal);
    }
}
