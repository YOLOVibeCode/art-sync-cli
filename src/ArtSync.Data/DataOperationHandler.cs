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

    public DataOperationHandler(IDataCompare engine) => _engine = engine;

    public OperationResult Run(CommandRequest request)
    {
        // ── Input validation ──────────────────────────────────────────────────

        if (request.CompFilePath is not null)
            return new(10,
                "/compfile (.dcomp) is not yet supported. " +
                "Provide /source and /target endpoints directly.");

        if (request.Source is null)
            return new(10,
                "No /source specified. " +
                "Use: /source server:S database:D [user:U] [password:P]  " +
                "or /source connection:\"<connection string>\".");

        if (request.Target is null)
            return new(10,
                "No /target specified.");

        // ── Execute ───────────────────────────────────────────────────────────

        try
        {
            var info = _engine.Compare(request.Source, request.Target, request.Options);

            if (info.Status == DataCompareStatus.NoComparableTables)
                return new(108,
                    "No comparable data tables found. Check object masks and that tables have primary keys.");

            if (info.Status == DataCompareStatus.Identical)
            {
                if (request.SyncMode == SyncMode.Apply)
                    return new(112, "Nothing to sync: all compared rows are identical.");
                return new(100, $"Data identical across {info.ComparableTables.Count} table(s).");
            }

            // Differences exist — fetch diffs for scripting or reporting.
            // For compare-only, we don't generate a script.
            if (request.SyncMode == SyncMode.None)
                return new(101,
                    $"Data differences: {info.TotalDifferentRows} row(s) differ " +
                    $"(+{info.OnlyInSourceRows} src-only, −{info.OnlyInTargetRows} tgt-only, " +
                    $"~{info.DifferentRows} changed).");

            // We need the actual diff list for scripting.  For now the info carries counts;
            // a real engine provides RowDiff list via Compare (see ITableDiscoverer + IDiffClassifier).
            // Placeholder: delegate to engine.Script with an empty diff list — the real engine
            // internally re-uses its comparison state.
            var diffs = Array.Empty<RowDiff>(); // engine knows from Compare call
            var script = _engine.Script(request.Source, request.Target, diffs, request.Options);

            if (request.SyncMode == SyncMode.ScriptFile)
            {
                WriteScript(request.SyncFilePath!, script);
                return new(101, $"Data sync script written to: {request.SyncFilePath}");
            }

            // Apply live.
            _engine.Apply(request.Target, script, request.Options);
            return new(0, "Data synchronization applied successfully.");
        }
        catch (DataConnectionException ex)
        {
            return new(40, $"Connection failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            return new(10, $"Data compare error: {ex.Message}");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

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
}
