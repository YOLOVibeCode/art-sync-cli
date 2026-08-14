using Microsoft.Data.SqlClient;

namespace ArtSync.Data;

/// <summary>
/// Reads table + column metadata from <c>sys.tables</c> / <c>sys.columns</c>.
/// Only tables that have at least one primary-key column are returned;
/// heaps without a usable key are silently skipped (per SPEC §10.1 DC-2).
/// </summary>
internal sealed class SqlMetadataReader
{
    // Lob types that are skipped from the hash payload in v1.
    private static readonly HashSet<string> LobTypes = new(StringComparer.OrdinalIgnoreCase)
        { "text", "ntext", "image", "xml" };

    private const string Query = """
        SELECT
            QUOTENAME(s.name) + N'.' + QUOTENAME(t.name)   AS QualifiedName,
            c.column_id,
            c.name                                          AS ColumnName,
            QUOTENAME(c.name)                               AS QuotedColumnName,
            tp.name                                         AS TypeName,
            c.is_nullable,
            c.is_identity,
            c.is_computed,
            c.is_rowguidcol,
            CASE WHEN tp.name IN ('timestamp','rowversion') THEN 1 ELSE 0 END AS IsTimestamp,
            CASE WHEN tp.name IN ('text','ntext','image','xml')
                 OR (tp.name IN ('varchar','nvarchar','varbinary') AND c.max_length = -1)
                 THEN 1 ELSE 0 END                          AS IsLob,
            CASE WHEN pk.column_id IS NOT NULL THEN 1 ELSE 0 END AS IsPrimaryKey
        FROM sys.tables t
        JOIN sys.schemas s  ON s.schema_id = t.schema_id
        JOIN sys.columns c  ON c.object_id = t.object_id
        JOIN sys.types   tp ON tp.user_type_id = c.user_type_id
        LEFT JOIN (
            SELECT i.object_id, ic.column_id
            FROM sys.indexes i
            JOIN sys.index_columns ic
                ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            WHERE i.is_primary_key = 1
        ) pk ON pk.object_id = c.object_id AND pk.column_id = c.column_id
        WHERE t.is_ms_shipped = 0
        ORDER BY s.name, t.name, c.column_id
        """;

    /// <summary>
    /// Returns all tables (with at least one PK column) from the database
    /// identified by <paramref name="connectionString"/>.
    /// </summary>
    public IReadOnlyList<SqlTableInfo> ReadTables(string connectionString)
    {
        var rows = new List<(string QualifiedName, SqlColumnInfo Col)>();

        using var conn = new SqlConnection(connectionString);
        conn.Open();
        using var cmd  = new SqlCommand(Query, conn);
        using var rdr  = cmd.ExecuteReader();

        while (rdr.Read())
        {
            bool IsTimestamp = Convert.ToBoolean(rdr["IsTimestamp"]);
            bool IsLob       = Convert.ToBoolean(rdr["IsLob"]);

            var col = new SqlColumnInfo(
                ColumnId:    (int)rdr["column_id"],
                Name:        (string)rdr["ColumnName"],
                QuotedName:  (string)rdr["QuotedColumnName"],
                TypeName:    (string)rdr["TypeName"],
                IsNullable:  Convert.ToBoolean(rdr["is_nullable"]),
                IsIdentity:  Convert.ToBoolean(rdr["is_identity"]),
                IsComputed:  Convert.ToBoolean(rdr["is_computed"]),
                IsRowguid:   Convert.ToBoolean(rdr["is_rowguidcol"]),
                IsTimestamp: IsTimestamp,
                IsLob:       IsLob,
                IsPrimaryKey:Convert.ToBoolean(rdr["IsPrimaryKey"]));

            rows.Add(((string)rdr["QualifiedName"], col));
        }

        return rows
            .GroupBy(r => r.QualifiedName)
            .Select(g =>
            {
                var allCols = g.Select(r => r.Col).ToList();
                var pkCols  = allCols.Where(c => c.IsPrimaryKey).ToList();
                if (pkCols.Count == 0) return null;   // skip heaps

                // Data columns: everything that is not a PK, not computed, not rowversion/timestamp.
                // LOBs are also excluded from hash comparison in v1.
                var dataCols = allCols
                    .Where(c => !c.IsPrimaryKey
                             && !c.IsComputed
                             && !c.IsTimestamp)
                    .ToList();

                return new SqlTableInfo(g.Key, pkCols, dataCols);
            })
            .Where(t => t is not null)
            .Select(t => t!)
            .ToList();
    }

    /// <summary>Returns just the qualified table names (for discovery).</summary>
    public IReadOnlyList<string> ReadTableNames(string connectionString)
        => ReadTables(connectionString).Select(t => t.QualifiedName).ToList();
}
