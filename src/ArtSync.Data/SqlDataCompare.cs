using ArtSync.Abstractions;

namespace ArtSync.Data;

/// <summary>
/// Production <see cref="IDataCompare"/> implementation backed by SQL Server.
///
/// Pipeline (SPEC §10.2):
///   1. Discover tables via <see cref="SqlMetadataReader"/> (skips heaps).
///   2. Stream <c>(pkKey, hash)</c> from each server using <see cref="SqlRowHashStreamer"/>.
///   3. Classify rows in-memory via <see cref="SqlDiffClassifier"/>.
///   4. Generate DML via <see cref="SqlDataScripter"/> when scripting/applying.
///   5. Apply with retry via <see cref="SqlDataApplier"/>.
/// </summary>
public sealed class SqlDataCompare : IDataCompare
{
    private readonly SqlMetadataReader  _meta       = new();
    private readonly SqlRowHashStreamer _hasher     = new();
    private readonly SqlDiffClassifier  _classifier = new();
    private readonly SqlDataScripter    _scripter   = new();
    private readonly SqlDataApplier     _applier    = new();

    // Stored after Compare() so Script() can use them without a second compare.
    private IReadOnlyList<RowDiff> _lastDiffs = [];

    public DataCompareInfo Compare(
        Endpoint source,
        Endpoint target,
        IReadOnlyDictionary<string, string> options)
    {
        var srcCs = BuildCs(source);
        var tgtCs = BuildCs(target);

        // Discover tables present in both databases.
        var srcTables = _meta.ReadTables(srcCs).ToDictionary(t => t.QualifiedName, StringComparer.OrdinalIgnoreCase);
        var tgtNames  = new HashSet<string>(_meta.ReadTableNames(tgtCs), StringComparer.OrdinalIgnoreCase);

        var comparable = srcTables.Values
            .Where(t => tgtNames.Contains(t.QualifiedName))
            .ToList();

        var skipped = srcTables.Values
            .Where(t => !tgtNames.Contains(t.QualifiedName))
            .Select(t => t.QualifiedName)
            .ToList();

        if (comparable.Count == 0)
        {
            _lastDiffs = [];
            return new DataCompareInfo(
                DataCompareStatus.NoComparableTables,
                0, 0, 0, 0, [], skipped);
        }

        // Hash + classify each table.
        var allDiffs = new List<RowDiff>();

        foreach (var table in comparable)
        {
            var srcHashes = _hasher.Stream(srcCs, table, options);
            var tgtHashes = _hasher.Stream(tgtCs, table, options);
            var diffs     = _classifier.Classify(table.QualifiedName, srcHashes, tgtHashes);
            allDiffs.AddRange(diffs);
        }

        _lastDiffs = allDiffs;

        if (allDiffs.Count == 0)
        {
            return new DataCompareInfo(
                DataCompareStatus.Identical,
                0, 0, 0, 0,
                comparable.Select(t => t.QualifiedName).ToList(),
                skipped);
        }

        return new DataCompareInfo(
            DataCompareStatus.HasDifferences,
            allDiffs.Count,
            allDiffs.Count(d => d.Kind == RowDiffKind.OnlyInSource),
            allDiffs.Count(d => d.Kind == RowDiffKind.OnlyInTarget),
            allDiffs.Count(d => d.Kind == RowDiffKind.Different),
            comparable.Select(t => t.QualifiedName).ToList(),
            skipped);
    }

    public string Script(
        Endpoint source,
        Endpoint target,
        IReadOnlyList<RowDiff> diffs,
        IReadOnlyDictionary<string, string> options)
    {
        // DataOperationHandler passes Array.Empty — use stored diffs from last Compare().
        var effectiveDiffs = diffs.Count > 0 ? diffs : _lastDiffs;
        return _scripter.Script(BuildCs(source), effectiveDiffs, options);
    }

    public void Apply(
        Endpoint target,
        string script,
        IReadOnlyDictionary<string, string> options)
    {
        _applier.Apply(BuildCs(target), script, options);
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    internal static string BuildCs(Endpoint ep)
    {
        if (ep.Kind == EndpointKind.ConnectionString)
        {
            if (string.IsNullOrWhiteSpace(ep.ConnectionString))
                throw new ArgumentException("Connection string is empty.");
            return ep.ConnectionString!;
        }

        if (string.IsNullOrWhiteSpace(ep.Server))   throw new ArgumentException("Missing server.");
        if (string.IsNullOrWhiteSpace(ep.Database)) throw new ArgumentException("Missing database.");

        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder
        {
            DataSource     = ep.Server,
            InitialCatalog = ep.Database,
        };

        if (!string.IsNullOrEmpty(ep.User))
        {
            builder.UserID   = ep.User;
            builder.Password = ep.Password ?? string.Empty;
            builder.IntegratedSecurity = false;
        }
        else
        {
            builder.IntegratedSecurity = true;
        }

        return builder.ConnectionString;
    }
}
