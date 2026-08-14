using Microsoft.Data.SqlClient;

namespace ArtSync.Data;

/// <summary>
/// Reads table + column metadata from <c>sys.tables</c> / <c>sys.columns</c>.
/// Comparison key is the primary key if present, otherwise the first unique
/// constraint/index whose columns are all NOT NULL (SPEC §10.1 DC-1).
/// Heaps with no usable key are returned with an empty <c>PkColumns</c> list
/// so the caller can skip them with a warning (DC-2).
/// </summary>
internal sealed class SqlMetadataReader
{
    private static string BuildColumnQuery(bool fromViews) => fromViews
        ? """
        SELECT
            QUOTENAME(s.name) + N'.' + QUOTENAME(v.name)   AS QualifiedName,
            c.column_id,
            c.name                                          AS ColumnName,
            QUOTENAME(c.name)                               AS QuotedColumnName,
            TYPE_NAME(c.system_type_id)                     AS TypeName,
            c.is_nullable,
            CAST(0 AS BIT)                                  AS is_identity,
            c.is_computed,
            CAST(0 AS BIT)                                  AS is_rowguidcol,
            CAST(0 AS BIT)                                  AS IsTimestamp,
            CASE WHEN TYPE_NAME(c.system_type_id) IN (N'text', N'ntext', N'image', N'xml')
                  OR (TYPE_NAME(c.system_type_id) IN (N'varchar', N'nvarchar', N'varbinary')
                      AND c.max_length = -1)
                 THEN 1 ELSE 0 END                          AS IsLob,
            CASE WHEN ik.column_id IS NOT NULL THEN 1 ELSE 0 END AS IsPrimaryKey,
            ISNULL(ik.key_ordinal, 0)                       AS KeyOrdinal
        FROM sys.views v
        JOIN sys.schemas s  ON s.schema_id = v.schema_id
        JOIN sys.columns c  ON c.object_id = v.object_id
        LEFT JOIN (
            SELECT i.object_id, ic.column_id, ic.key_ordinal
            FROM sys.indexes i
            JOIN sys.index_columns ic
                ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            WHERE i.is_unique = 1
              AND i.has_filter = 0
              AND ic.is_included_column = 0
        ) ik ON ik.object_id = c.object_id AND ik.column_id = c.column_id
        WHERE v.is_ms_shipped = 0
        ORDER BY s.name, v.name, c.column_id
        """
        : """
        SELECT
            QUOTENAME(s.name) + N'.' + QUOTENAME(t.name)   AS QualifiedName,
            c.column_id,
            c.name                                          AS ColumnName,
            QUOTENAME(c.name)                               AS QuotedColumnName,
            TYPE_NAME(c.system_type_id)                     AS TypeName,
            c.is_nullable,
            c.is_identity,
            c.is_computed,
            c.is_rowguidcol,
            CASE WHEN TYPE_NAME(c.system_type_id) IN (N'timestamp', N'rowversion')
                 THEN 1 ELSE 0 END                          AS IsTimestamp,
            CASE WHEN TYPE_NAME(c.system_type_id) IN (N'text', N'ntext', N'image', N'xml')
                  OR (TYPE_NAME(c.system_type_id) IN (N'varchar', N'nvarchar', N'varbinary')
                      AND c.max_length = -1)
                 THEN 1 ELSE 0 END                          AS IsLob,
            CASE WHEN pk.column_id IS NOT NULL THEN 1 ELSE 0 END AS IsPrimaryKey,
            ISNULL(pk.key_ordinal, 0)                       AS KeyOrdinal
        FROM sys.tables t
        JOIN sys.schemas s  ON s.schema_id = t.schema_id
        JOIN sys.columns c  ON c.object_id = t.object_id
        LEFT JOIN (
            SELECT i.object_id, ic.column_id, ic.key_ordinal
            FROM sys.indexes i
            JOIN sys.index_columns ic
                ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            WHERE i.is_primary_key = 1
              AND ic.is_included_column = 0
        ) pk ON pk.object_id = c.object_id AND pk.column_id = c.column_id
        WHERE t.is_ms_shipped = 0
        ORDER BY s.name, t.name, c.column_id
        """;

    private const string UniqueKeyQuery = """
        SELECT
            QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) AS QualifiedName,
            i.index_id,
            ic.key_ordinal,
            c.name AS ColumnName,
            c.is_nullable
        FROM sys.tables t
        JOIN sys.schemas s ON s.schema_id = t.schema_id
        JOIN sys.indexes i ON i.object_id = t.object_id
        JOIN sys.index_columns ic
            ON ic.object_id = i.object_id AND ic.index_id = i.index_id
        JOIN sys.columns c
            ON c.object_id = t.object_id AND c.column_id = ic.column_id
        WHERE t.is_ms_shipped = 0
          AND i.is_unique = 1
          AND i.is_primary_key = 0
          AND i.has_filter = 0
          AND ic.is_included_column = 0
        ORDER BY i.index_id, ic.key_ordinal
        """;

    public IReadOnlyList<SqlTableInfo> ReadTables(
        string connectionString,
        bool includeTables = true,
        bool includeViews = false)
    {
        var rows = new List<(string QualifiedName, SqlColumnInfo Col)>();
        if (includeTables)
            rows.AddRange(ReadColumns(connectionString, fromViews: false));
        if (includeViews)
            rows.AddRange(ReadColumns(connectionString, fromViews: true));

        if (rows.Count == 0) return [];

        var uniqueKeys = ReadBestUniqueKeys(connectionString);

        return rows
            .GroupBy(r => r.QualifiedName)
            .Select(g => BuildTable(g.Key, g.Select(r => r.Col).ToList(), uniqueKeys))
            .ToList();
    }

    public IReadOnlyList<string> ReadTableNames(string connectionString)
        => ReadTables(connectionString).Select(t => t.QualifiedName).ToList();

    // ── Internals ─────────────────────────────────────────────────────────────

    private static SqlTableInfo BuildTable(
        string qualifiedName,
        List<SqlColumnInfo> allCols,
        IReadOnlyDictionary<string, IReadOnlyList<string>> uniqueKeys)
    {
        var pkCols = allCols
            .Where(c => c.IsPrimaryKey)
            .OrderBy(c => c.KeyOrdinal)
            .ToList();

        if (pkCols.Count == 0
            && uniqueKeys.TryGetValue(qualifiedName, out var keyNames)
            && keyNames.Count > 0)
        {
            pkCols = keyNames
                .Select((name, i) =>
                {
                    var col = allCols.First(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    return col with { IsPrimaryKey = true, KeyOrdinal = i + 1 };
                })
                .ToList();
        }

        var pkNames = pkCols.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dataCols = allCols
            .Where(c => !pkNames.Contains(c.Name) && !c.IsComputed && !c.IsTimestamp)
            .ToList();

        return new SqlTableInfo(qualifiedName, pkCols, dataCols);
    }

    private static List<(string QualifiedName, SqlColumnInfo Col)> ReadColumns(string cs, bool fromViews = false)
    {
        var rows = new List<(string QualifiedName, SqlColumnInfo Col)>();
        using var conn = new SqlConnection(cs);
        conn.Open();
        using var cmd = new SqlCommand(BuildColumnQuery(fromViews), conn);
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
        {
            var col = new SqlColumnInfo(
                ColumnId:     (int)rdr["column_id"],
                Name:         (string)rdr["ColumnName"],
                QuotedName:   (string)rdr["QuotedColumnName"],
                TypeName:     (string)rdr["TypeName"],
                IsNullable:   Convert.ToBoolean(rdr["is_nullable"]),
                IsIdentity:   Convert.ToBoolean(rdr["is_identity"]),
                IsComputed:   Convert.ToBoolean(rdr["is_computed"]),
                IsRowguid:    Convert.ToBoolean(rdr["is_rowguidcol"]),
                IsTimestamp:  Convert.ToBoolean(rdr["IsTimestamp"]),
                IsLob:        Convert.ToBoolean(rdr["IsLob"]),
                IsPrimaryKey: Convert.ToBoolean(rdr["IsPrimaryKey"]),
                KeyOrdinal:   Convert.ToInt32(rdr["KeyOrdinal"]));
            rows.Add(((string)rdr["QualifiedName"], col));
        }
        return rows;
    }

    /// <summary>
    /// For tables without a PK, the unique index/constraint with the fewest
    /// columns whose members are all NOT NULL.
    /// </summary>
    private static Dictionary<string, IReadOnlyList<string>> ReadBestUniqueKeys(string cs)
    {
        var raw = new List<(string Table, int IndexId, int Ordinal, string Col, bool Nullable)>();
        using var conn = new SqlConnection(cs);
        conn.Open();
        using var cmd = new SqlCommand(UniqueKeyQuery, conn);
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
        {
            raw.Add((
                (string)rdr["QualifiedName"],
                (int)rdr["index_id"],
                Convert.ToInt32(rdr["key_ordinal"]),
                (string)rdr["ColumnName"],
                Convert.ToBoolean(rdr["is_nullable"])));
        }

        var best = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var tableGroup in raw.GroupBy(r => r.Table, StringComparer.OrdinalIgnoreCase))
        {
            var candidate = tableGroup
                .GroupBy(r => r.IndexId)
                .Select(g => g.OrderBy(x => x.Ordinal).ToList())
                .Where(cols => cols.All(c => !c.Nullable))
                .OrderBy(cols => cols.Count)
                .ThenBy(cols => cols[0].IndexId)
                .FirstOrDefault();

            if (candidate is { Count: > 0 })
                best[tableGroup.Key] = candidate.Select(c => c.Col).ToList();
        }

        return best;
    }
}
