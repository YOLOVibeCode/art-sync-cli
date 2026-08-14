using ArtSync.Abstractions;

namespace ArtSync.Data;

/// <summary>
/// Handles <see cref="OperationType.DataCompare"/> requests.
/// Routes compare / script / apply to <see cref="IDataCompare"/> and maps
/// outcomes to Devart-compatible exit codes (SPEC §8).
/// </summary>
public sealed class DataOperationHandler : IOperationHandler
{
    private readonly IDataCompare _engine;
    private readonly Func<string, IOperationLogger>? _loggerFactory;
    private readonly IResultReporter? _reporter;

    public DataOperationHandler(
        IDataCompare engine,
        Func<string, IOperationLogger>? loggerFactory = null,
        IResultReporter? reporter = null)
    {
        _engine = engine;
        _loggerFactory = loggerFactory;
        _reporter = reporter;
    }

    public OperationResult Run(CommandRequest request)
    {
        IOperationLogger logger = NullOperationLogger.Instance;
        try
        {
            // ── Open log ──────────────────────────────────────────────────────
            if (request.LogPath is not null && _loggerFactory is not null)
                logger = _loggerFactory(request.LogPath);

            logger.LogLine($"START datacompare argv0={request.Argv0}");
            logger.LogLine($"  source={DescribeEndpoint(request.Source)}");
            logger.LogLine($"  target={DescribeEndpoint(request.Target)}");
            logger.LogLine($"  sync={request.SyncMode}");

            // ── Input validation ──────────────────────────────────────────────

            if (request.CompFilePath is not null)
                return Finish(logger, new(10,
                    "/compfile (.dcomp) is not yet supported. " +
                    "Provide /source and /target endpoints directly."));

            if (request.Source is null)
                return Finish(logger, new(10,
                    "No /source specified. " +
                    "Use: /source server:S database:D [user:U] [password:P]  " +
                    "or /source connection:\"<connection string>\"."));

            if (request.Target is null)
                return Finish(logger, new(10, "No /target specified."));

            // ── Execute ───────────────────────────────────────────────────────

            var info = _engine.Compare(request.Source, request.Target, request.Options);
            logger.LogLine(
                $"  compare done: tables={info.ComparableTables.Count} " +
                $"srcOnly={info.OnlyInSourceRows} tgtOnly={info.OnlyInTargetRows} " +
                $"diff={info.DifferentRows}");

            if (info.SkippedTables.Count > 0)
                logger.LogLine($"  skipped tables: {string.Join(", ", info.SkippedTables)}");

            // ── Row diffs for reports (from the Compare we just ran) ──────────
            var diffs = _engine.LastDiffs;

            // ── Write report ──────────────────────────────────────────────────
            if (request.ReportPath is not null && _reporter is not null)
            {
                var fmt = request.ReportFormat ?? "";
                _reporter.WriteDataReport(request.ReportPath, fmt, info, diffs, request);
                logger.LogLine($"  report written to {request.ReportPath}");
            }

            // ── Determine result ──────────────────────────────────────────────

            if (info.Status == DataCompareStatus.NoComparableTables)
                return Finish(logger, new(108,
                    "No comparable data tables found. Check object masks and that tables have primary keys."));

            if (info.Status == DataCompareStatus.Identical)
            {
                if (request.SyncMode == SyncMode.Apply)
                    return Finish(logger, new(112, "Nothing to sync: all compared rows are identical."));
                return Finish(logger, new(100, $"Data identical across {info.ComparableTables.Count} table(s)."));
            }

            // Differences exist.
            if (request.SyncMode == SyncMode.None)
                return Finish(logger, new(101,
                    $"Data differences: {info.TotalDifferentRows} row(s) differ " +
                    $"(+{info.OnlyInSourceRows} src-only, \u2212{info.OnlyInTargetRows} tgt-only, " +
                    $"~{info.DifferentRows} changed)."));

            var script = _engine.Script(request.Source, request.Target, diffs, request.Options);

            if (request.SyncMode == SyncMode.ScriptFile)
            {
                WriteScript(request.SyncFilePath!, script);
                logger.LogLine($"  script written to {request.SyncFilePath}");
                return Finish(logger, new(101, $"Data sync script written to: {request.SyncFilePath}"));
            }

            _engine.Apply(request.Target, script, request.Options);
            logger.LogLine("  apply succeeded");
            return Finish(logger, new(0, "Data synchronization applied successfully."));
        }
        catch (LogIoException ex)
        {
            return new(106, $"Log I/O error: {ex.Message}");
        }
        catch (ReportIoException ex)
        {
            logger.LogLine($"  REPORT ERROR: {ex.Message}");
            return Finish(logger, new(107, $"Report write error: {ex.Message}"));
        }
        catch (DataConnectionException ex)
        {
            logger.LogLine($"  CONNECTION ERROR: {ex.Message}");
            return Finish(logger, new(40, $"Connection failed: {ex.Message}"));
        }
        catch (Exception ex)
        {
            logger.LogLine($"  ERROR: {ex.Message}");
            return Finish(logger, new(10, $"Data compare error: {ex.Message}"));
        }
        finally
        {
            if (logger is not NullOperationLogger) logger.Dispose();
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static OperationResult Finish(IOperationLogger logger, OperationResult result)
    {
        logger.LogLine($"  EXIT {result.ExitCode}");
        return result;
    }

    private static void WriteScript(string path, string content)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, content);
        }
        catch (Exception ex)
        {
            throw new Exception($"Cannot write data sync script to '{path}': {ex.Message}", ex);
        }
    }

    private static string DescribeEndpoint(Endpoint? ep)
    {
        if (ep is null) return "(none)";
        if (ep.Kind == EndpointKind.ConnectionString)
            return ep.ConnectionString ?? "";
        return $"{ep.Server}/{ep.Database}";
    }
}
