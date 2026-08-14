namespace ArtSync.Abstractions;

// ── Domain types ──────────────────────────────────────────────────────────────

/// <summary>How a table was discovered for data comparison.</summary>
public enum DataCompareStatus
{
    Identical,
    HasDifferences,
    NoComparableTables,
}

/// <summary>How a row differs between source and target.</summary>
public enum RowDiffKind
{
    OnlyInSource,   // INSERT on apply
    OnlyInTarget,   // DELETE on apply
    Different,      // UPDATE on apply
    Identical,      // no action
}

/// <summary>Represents a single row difference keyed by primary-key values.</summary>
public record RowDiff(
    string TableName,
    RowDiffKind Kind,
    IReadOnlyList<(string Column, object? Value)> PkValues
);

/// <summary>Summary of a data comparison outcome.</summary>
public record DataCompareInfo(
    DataCompareStatus Status,
    int TotalDifferentRows,
    int OnlyInSourceRows,
    int OnlyInTargetRows,
    int DifferentRows,
    IReadOnlyList<string> ComparableTables,
    IReadOnlyList<string> SkippedTables   // heaps without PK, LOB-only tables, etc.
);

// ── ISP-segregated interfaces ─────────────────────────────────────────────────

/// <summary>
/// Discovers which tables/views in source and target are comparable and maps
/// their names according to MappingIgnoreCase/Spaces/Underscores options.
/// Callers that only discover tables depend only on this interface.
/// </summary>
public interface ITableDiscoverer
{
    IReadOnlyList<TablePair> DiscoverComparableTables(
        Endpoint source,
        Endpoint target,
        IReadOnlyDictionary<string, string> options);
}

/// <summary>A matched (source, target) table pair.</summary>
public record TablePair(string SourceTable, string TargetTable);

/// <summary>
/// Streams (pk, hash) pairs from a single server-side table.
/// Does NOT pull full row data; uses HASHBYTES('SHA2_256', …) on the server.
/// Callers that only stream hashes depend only on this interface.
/// </summary>
public interface IRowHashStream
{
    /// <summary>
    /// Yields (pk-concatenated-value, sha256-hash) pairs ordered by PK for
    /// merge-join by <see cref="IDiffClassifier"/>.
    /// </summary>
    IEnumerable<(string PkKey, byte[] Hash)> Stream(
        Endpoint endpoint,
        TablePair table,
        IReadOnlyDictionary<string, string> options);
}

/// <summary>
/// Merge-joins source and target hash streams to classify rows.
/// No SQL is run here; this is pure in-memory set arithmetic.
/// </summary>
public interface IDiffClassifier
{
    IReadOnlyList<RowDiff> Classify(
        string tableName,
        IEnumerable<(string PkKey, byte[] Hash)> source,
        IEnumerable<(string PkKey, byte[] Hash)> target,
        IReadOnlyDictionary<string, string> options);
}

/// <summary>
/// Fetches full row payloads (only for rows that WILL be scripted) and
/// generates T-SQL INSERT / UPDATE / DELETE / MERGE statements.
/// Depends only on row diffs, not on raw hash streams.
/// </summary>
public interface IDataScripter
{
    string Script(
        Endpoint source,
        Endpoint target,
        IReadOnlyList<RowDiff> diffs,
        IReadOnlyDictionary<string, string> options);
}

/// <summary>
/// Applies a T-SQL sync script to the live target with transient-fault retry.
/// Depends only on the script string and the target endpoint.
/// </summary>
public interface IDataApplier
{
    void Apply(
        Endpoint target,
        string script,
        IReadOnlyDictionary<string, string> options);
}

/// <summary>
/// Facade that composes the four segregated interfaces for callers that need
/// the full compare → script → apply pipeline (e.g. DataOperationHandler).
/// Split it when a caller only needs one stage.
/// </summary>
public interface IDataCompare
{
    DataCompareInfo Compare(
        Endpoint source,
        Endpoint target,
        IReadOnlyDictionary<string, string> options);

    string Script(
        Endpoint source,
        Endpoint target,
        IReadOnlyList<RowDiff> diffs,
        IReadOnlyDictionary<string, string> options);

    void Apply(
        Endpoint target,
        string script,
        IReadOnlyDictionary<string, string> options);
}

/// <summary>
/// Strongly-typed exception for data-compare connection failures (exit 40).
/// </summary>
public sealed class DataConnectionException : Exception
{
    public DataConnectionException(string message, Exception? inner = null)
        : base(message, inner) { }
}
