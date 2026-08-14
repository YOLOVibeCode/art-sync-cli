using ArtSync.Abstractions;

namespace ArtSync.Schema;

/// <summary>
/// Handles <see cref="OperationType.SchemaCompare"/> requests.
/// Routes compare / script / apply to <see cref="ISchemaCompare"/> and maps
/// outcomes to Devart-compatible exit codes (SPEC §8).
/// </summary>
public sealed class SchemaOperationHandler : IOperationHandler
{
    private readonly ISchemaCompare _engine;
    private readonly Func<string, IOperationLogger>? _loggerFactory;
    private readonly IResultReporter? _reporter;

    public SchemaOperationHandler(
        ISchemaCompare engine,
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

            logger.LogLine($"START schemacompare argv0={request.Argv0}");
            logger.LogLine($"  source={DescribeEndpoint(request.Source)}");
            logger.LogLine($"  target={DescribeEndpoint(request.Target)}");
            logger.LogLine($"  sync={request.SyncMode}");

            // ── Input validation ──────────────────────────────────────────────

            if (request.CompFilePath is not null)
                return Finish(logger, new(10,
                    "/compfile (.scomp) is not yet supported. " +
                    "Provide /source and /target endpoints directly."));

            if (request.Source is null)
                return Finish(logger, new(10,
                    "No /source specified. " +
                    "Use: /source server:S database:D [user:U] [password:P]  " +
                    "or /source connection:\"<connection string>\"."));

            if (request.Target is null)
                return Finish(logger, new(10,
                    "No /target specified. " +
                    "Use: /target server:S database:D [user:U] [password:P]  " +
                    "or /target connection:\"<connection string>\"."));

            // ── Execute ───────────────────────────────────────────────────────

            IReadOnlyDictionary<string, string> options = request.Options;
            if (request.FilterFilePath is not null)
            {
                var merged = new Dictionary<string, string>(request.Options)
                {
                    ["_FilterFilePath"] = request.FilterFilePath,
                };
                options = merged;
            }

            using var session = _engine.OpenSession(request.Source, request.Target, options);

            var info = session.Compare();
            logger.LogLine($"  compare done: identical={info.IsIdentical} diffs={info.DifferenceCount}");

            // ── Write report ──────────────────────────────────────────────────
            if (request.ReportPath is not null && _reporter is not null)
            {
                var fmt = request.ReportFormat ?? "";
                _reporter.WriteSchemaReport(request.ReportPath, fmt, info, request);
                logger.LogLine($"  report written to {request.ReportPath}");
            }

            // ── Determine result ──────────────────────────────────────────────

            if (info.HasNoComparableObjects)
                return Finish(logger, new(108,
                    "No comparable schema objects found after applying filters."));

            if (info.IsIdentical)
            {
                if (request.SyncMode == SyncMode.Apply)
                    return Finish(logger, new(112, "Nothing to sync: source and target schemas are identical."));
                return Finish(logger, new(100, "Source and target schemas are identical."));
            }

            if (request.SyncMode == SyncMode.None)
                return Finish(logger, new(101,
                    $"Schema differences found: {info.DifferenceCount} object(s) differ."));

            if (request.SyncMode == SyncMode.ScriptFile)
            {
                var script = session.GenerateScript();
                if (script is null or { Length: 0 })
                    script = "-- No changes generated";
                WriteScript(request.SyncFilePath!, script);
                logger.LogLine($"  script written to {request.SyncFilePath}");
                return Finish(logger, new(101,
                    $"Sync script written to: {request.SyncFilePath}"));
            }

            session.Publish();
            logger.LogLine("  publish succeeded");
            return Finish(logger, new(0, "Schema synchronization applied successfully."));
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
        catch (SchemaFilterException ex)
        {
            logger.LogLine($"  FILTER ERROR: {ex.Message}");
            return Finish(logger, new(114, $"Filter file error: {ex.Message}"));
        }
        catch (SchemaConnectionException ex)
        {
            logger.LogLine($"  CONNECTION ERROR: {ex.Message}");
            return Finish(logger, new(40, $"Connection failed: {ex.Message}"));
        }
        catch (SchemaIoException ex)
        {
            logger.LogLine($"  IO ERROR: {ex.Message}");
            return Finish(logger, new(106, $"I/O error: {ex.Message}"));
        }
        catch (Exception ex)
        {
            logger.LogLine($"  ERROR: {ex.Message}");
            return Finish(logger, new(10, $"Schema compare error: {ex.Message}"));
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
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, content);
        }
        catch (Exception ex)
        {
            throw new SchemaIoException($"Cannot write script to '{path}': {ex.Message}", ex);
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
