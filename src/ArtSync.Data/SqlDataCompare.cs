using ArtSync.Abstractions;
using Microsoft.Data.SqlClient;

namespace ArtSync.Data;

/// <summary>
/// Production <see cref="IDataCompare"/> implementation backed by SQL Server.
///
/// Pipeline (SPEC §10.2):
///   1. Discover tables (PK or unique key; skip heaps).
///   2. Apply object masks.
///   3. Stream hashes; classify; honour CheckOnlyIn* / CheckDifferent.
///   4. Script in FK-safe order.
///   5. Apply with retry.
/// </summary>
public sealed class SqlDataCompare : IDataCompare
{
    private readonly SqlMetadataReader  _meta       = new();
    private readonly SqlRowHashStreamer _hasher     = new();
    private readonly SqlDiffClassifier  _classifier = new();
    private readonly SqlDataScripter    _scripter   = new();
    private readonly SqlDataApplier     _applier    = new();

    private IReadOnlyList<RowDiff> _lastDiffs = [];

    public IReadOnlyList<RowDiff> LastDiffs => _lastDiffs;

    public DataCompareInfo Compare(
        Endpoint source,
        Endpoint target,
        IReadOnlyDictionary<string, string> options)
    {
        try
        {
            return CompareCore(source, target, options);
        }
        catch (SqlException ex) when (IsConnectionFailure(ex))
        {
            throw new DataConnectionException(ex.Message, ex);
        }
    }

    private static bool IsConnectionFailure(SqlException ex)
    {
        foreach (SqlError err in ex.Errors)
        {
            if (err.Number is 53 or 2 or -1 or 233 or 64
                or 4060 or 18456 or 18452
                or 10053 or 10054 or 10060 or 40613)
                return true;
        }

        var m = ex.Message;
        return m.Contains("network", StringComparison.OrdinalIgnoreCase)
            || m.Contains("login failed", StringComparison.OrdinalIgnoreCase)
            || m.Contains("server was not found", StringComparison.OrdinalIgnoreCase)
            || m.Contains("could not open a connection", StringComparison.OrdinalIgnoreCase);
    }

    private DataCompareInfo CompareCore(
        Endpoint source,
        Endpoint target,
        IReadOnlyDictionary<string, string> options)
    {
        var srcCs = BuildCs(source);
        var tgtCs = BuildCs(target);

        bool includeTables = SqlOptionFlags.IsOn(options, "CompareTables", defaultOn: true);
        bool includeViews  = SqlOptionFlags.IsOn(options, "CompareViews",  defaultOn: false);

        var srcTables = _meta.ReadTables(srcCs, includeTables, includeViews)
            .ToDictionary(t => t.QualifiedName, StringComparer.OrdinalIgnoreCase);
        var tgtByKey = _meta.ReadTables(tgtCs, includeTables, includeViews)
            .ToDictionary(t => MapKey(t.QualifiedName, options), StringComparer.OrdinalIgnoreCase);

        var skipped = new List<string>();
        var comparable = new List<SqlTableInfo>();

        foreach (var src in srcTables.Values)
        {
            if (!SqlNameMask.IsTableIncluded(src.QualifiedName, options))
                continue;

            if (src.PkColumns.Count == 0)
            {
                skipped.Add(src.QualifiedName);
                continue;
            }

            var key = MapKey(src.QualifiedName, options);
            if (!tgtByKey.TryGetValue(key, out var tgt))
            {
                skipped.Add(src.QualifiedName);
                continue;
            }

            if (!KeysMatch(src, tgt))
            {
                skipped.Add(src.QualifiedName);
                continue;
            }

            comparable.Add(AlignDataColumns(src, tgt) with
            {
                TargetQualifiedName = tgt.QualifiedName,
            });
        }

        if (comparable.Count == 0)
        {
            _lastDiffs = [];
            return new DataCompareInfo(
                DataCompareStatus.NoComparableTables,
                0, 0, 0, 0, [], skipped);
        }

        var allDiffs = new List<RowDiff>();
        foreach (var table in comparable)
        {
            var srcHashes = _hasher.Stream(srcCs, table, options);
            var tgtTable  = table with { QualifiedName = table.ApplyName };
            var tgtHashes = _hasher.Stream(tgtCs, tgtTable, options);
            allDiffs.AddRange(_classifier.Classify(table.QualifiedName, srcHashes, tgtHashes));
        }

        allDiffs = FilterSelectedKinds(allDiffs, options).ToList();
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
        var effectiveDiffs = diffs.Count > 0 ? diffs : _lastDiffs;
        return _scripter.Script(BuildCs(source), BuildCs(target), effectiveDiffs, options);
    }

    public void Apply(
        Endpoint target,
        string script,
        IReadOnlyDictionary<string, string> options)
        => _applier.Apply(BuildCs(target), script, options);

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

        var builder = new SqlConnectionStringBuilder
        {
            DataSource             = ep.Server,
            InitialCatalog         = ep.Database,
            Encrypt                = true,
            TrustServerCertificate = true,
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

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Normalises a qualified name for MappingIgnoreCase / Spaces / Underscores.
    /// Dictionary lookups are already case-insensitive.
    /// </summary>
    internal static string MapKey(string qualifiedName, IReadOnlyDictionary<string, string> options)
    {
        var n = qualifiedName.Replace("[", "", StringComparison.Ordinal)
                             .Replace("]", "", StringComparison.Ordinal);
        if (SqlOptionFlags.IsOn(options, "MappingIgnoreSpaces", defaultOn: false))
            n = n.Replace(" ", "", StringComparison.Ordinal);
        if (SqlOptionFlags.IsOn(options, "MappingIgnoreUnderscores", defaultOn: false))
            n = n.Replace("_", "", StringComparison.Ordinal);
        return n;
    }

    private static bool KeysMatch(SqlTableInfo src, SqlTableInfo tgt)
    {
        if (src.PkColumns.Count != tgt.PkColumns.Count) return false;
        return src.PkColumns
            .Zip(tgt.PkColumns, (a, b) =>
                a.Name.Equals(b.Name, StringComparison.OrdinalIgnoreCase)
                && a.TypeName.Equals(b.TypeName, StringComparison.OrdinalIgnoreCase))
            .All(eq => eq);
    }

    private static SqlTableInfo AlignDataColumns(SqlTableInfo src, SqlTableInfo tgt)
    {
        var tgtNames = tgt.DataColumns
            .Select(c => c.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var data = src.DataColumns.Where(c => tgtNames.Contains(c.Name)).ToList();
        return src with { DataColumns = data };
    }

    private static IEnumerable<RowDiff> FilterSelectedKinds(
        IEnumerable<RowDiff> diffs,
        IReadOnlyDictionary<string, string> options)
    {
        bool src  = SqlOptionFlags.IsOn(options, "CheckOnlyInSource", defaultOn: true);
        bool tgt  = SqlOptionFlags.IsOn(options, "CheckOnlyInTarget", defaultOn: true);
        bool diff = SqlOptionFlags.IsOn(options, "CheckDifferent",    defaultOn: true);

        foreach (var d in diffs)
        {
            var keep = d.Kind switch
            {
                RowDiffKind.OnlyInSource => src,
                RowDiffKind.OnlyInTarget => tgt,
                RowDiffKind.Different    => diff,
                _                        => false,
            };
            if (keep) yield return d;
        }
    }
}
