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

    public SchemaOperationHandler(ISchemaCompare engine) => _engine = engine;

    public OperationResult Run(CommandRequest request)
    {
        // ── Input validation ──────────────────────────────────────────────────

        if (request.CompFilePath is not null)
            return new(10,
                "/compfile (.scomp) is not yet supported. " +
                "Provide /source and /target endpoints directly.");

        if (request.Source is null)
            return new(10,
                "No /source specified. " +
                "Use: /source server:S database:D [user:U] [password:P]  " +
                "or /source connection:\"<connection string>\".");

        if (request.Target is null)
            return new(10,
                "No /target specified. " +
                "Use: /target server:S database:D [user:U] [password:P]  " +
                "or /target connection:\"<connection string>\".");

        // ── Execute ───────────────────────────────────────────────────────────

        try
        {
            // Pass filter path via a private key in the options dictionary so
            // ISchemaCompare can load it without changing the interface signature.
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

            if (info.HasNoComparableObjects)
                return new(108,
                    "No comparable schema objects found after applying filters.");

            if (info.IsIdentical)
            {
                if (request.SyncMode == SyncMode.Apply)
                    return new(112, "Nothing to sync: source and target schemas are identical.");
                return new(100, "Source and target schemas are identical.");
            }

            // Differences exist.
            if (request.SyncMode == SyncMode.None)
                return new(101,
                    $"Schema differences found: {info.DifferenceCount} object(s) differ.");

            if (request.SyncMode == SyncMode.ScriptFile)
            {
                var script = session.GenerateScript();
                if (script is null or { Length: 0 })
                    script = "-- No changes generated";
                WriteScript(request.SyncFilePath!, script);
                return new(101,
                    $"Sync script written to: {request.SyncFilePath}");
            }

            // Apply live.
            session.Publish();
            return new(0, "Schema synchronization applied successfully.");
        }
        catch (SchemaFilterException ex)
        {
            return new(114, $"Filter file error: {ex.Message}");
        }
        catch (SchemaConnectionException ex)
        {
            return new(40, $"Connection failed: {ex.Message}");
        }
        catch (SchemaIoException ex)
        {
            return new(106, $"I/O error: {ex.Message}");
        }
        catch (Exception ex)
        {
            return new(10, $"Schema compare error: {ex.Message}");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

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
}
