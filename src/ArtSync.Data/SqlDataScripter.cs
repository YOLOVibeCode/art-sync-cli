using System.Text;
using ArtSync.Abstractions;
using Microsoft.Data.SqlClient;

namespace ArtSync.Data;

/// <summary>
/// Fetches the actual row payloads for differing rows from the source and generates
/// T-SQL INSERT / UPDATE / DELETE statements to bring the target in sync.
///
/// Batches PK lookups to avoid per-row round-trips.
/// LOB columns are included in the DML even though they are excluded from the hash.
/// </summary>
internal sealed class SqlDataScripter
{
    private readonly SqlMetadataReader _meta = new();

    public string Script(
        string sourceCs,
        IReadOnlyList<RowDiff> diffs,
        IReadOnlyDictionary<string, string> options)
    {
        if (diffs.Count == 0) return "-- No data differences.";

        var sb = new StringBuilder();
        sb.AppendLine("-- ArtSync data sync script");
        sb.AppendLine($"-- Generated: {DateTime.UtcNow:O}");
        sb.AppendLine();

        var tableGroups = diffs.GroupBy(d => d.TableName);
        var allTableInfo = _meta.ReadTables(sourceCs)
            .ToDictionary(t => t.QualifiedName, StringComparer.OrdinalIgnoreCase);

        foreach (var group in tableGroups)
        {
            if (!allTableInfo.TryGetValue(group.Key, out var tableInfo))
            {
                sb.AppendLine($"-- WARNING: table {group.Key} not found in source; skipped.");
                continue;
            }

            ScriptTable(sb, sourceCs, tableInfo, group.ToList(), options);
        }

        return sb.ToString();
    }

    // ── Per-table scripting ───────────────────────────────────────────────────

    private static void ScriptTable(
        StringBuilder sb,
        string sourceCs,
        SqlTableInfo tableInfo,
        IReadOnlyList<RowDiff> diffs,
        IReadOnlyDictionary<string, string> options)
    {
        bool hasIdentity = tableInfo.PkColumns.Any(c => c.IsIdentity)
                        || tableInfo.DataColumns.Any(c => c.IsIdentity);

        var inserts = diffs.Where(d => d.Kind == RowDiffKind.OnlyInSource).ToList();
        var updates = diffs.Where(d => d.Kind == RowDiffKind.Different).ToList();
        var deletes = diffs.Where(d => d.Kind == RowDiffKind.OnlyInTarget).ToList();

        sb.AppendLine($"-- Table: {tableInfo.QualifiedName}");
        sb.AppendLine($"--   +{inserts.Count} inserts  ~{updates.Count} updates  -{deletes.Count} deletes");

        // ── INSERT rows only in source ────────────────────────────────────────
        if (inserts.Count > 0)
        {
            var rows = FetchRows(sourceCs, tableInfo, inserts);
            if (hasIdentity)
                sb.AppendLine($"SET IDENTITY_INSERT {tableInfo.QualifiedName} ON;");

            foreach (var row in rows)
                sb.AppendLine(BuildInsert(tableInfo, row));

            if (hasIdentity)
                sb.AppendLine($"SET IDENTITY_INSERT {tableInfo.QualifiedName} OFF;");
        }

        // ── UPDATE rows that differ ───────────────────────────────────────────
        if (updates.Count > 0)
        {
            var rows = FetchRows(sourceCs, tableInfo, updates);
            foreach (var row in rows)
                sb.AppendLine(BuildUpdate(tableInfo, row));
        }

        // ── DELETE rows only in target ────────────────────────────────────────
        foreach (var diff in deletes)
            sb.AppendLine(BuildDelete(tableInfo, diff));

        sb.AppendLine();
    }

    // ── Row fetching ──────────────────────────────────────────────────────────

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> FetchRows(
        string cs,
        SqlTableInfo tableInfo,
        IReadOnlyList<RowDiff> diffs)
    {
        if (diffs.Count == 0) return [];

        // Build WHERE pk IN (...) clause.
        // For single-column integer PKs this is clean.
        // For multi-column PKs we use OR-joined equality predicates.
        var sb = new StringBuilder();
        sb.Append($"SELECT * FROM {tableInfo.QualifiedName} WHERE ");

        if (tableInfo.PkColumns.Count == 1)
        {
            var pkCol = tableInfo.PkColumns[0];
            var values = diffs
                .Select(d => d.PkValues.FirstOrDefault(p => p.Column == pkCol.Name).Value?.ToString() ?? "NULL")
                .Select(v => SqlLiteral(v, pkCol.TypeName));
            sb.Append($"{pkCol.QuotedName} IN ({string.Join(", ", values)})");
        }
        else
        {
            var predicates = diffs.Select(d =>
            {
                var clauses = tableInfo.PkColumns.Select(pkCol =>
                {
                    var val = d.PkValues.FirstOrDefault(p => p.Column == pkCol.Name).Value?.ToString() ?? "NULL";
                    return $"{pkCol.QuotedName} = {SqlLiteral(val, pkCol.TypeName)}";
                });
                return $"({string.Join(" AND ", clauses)})";
            });
            sb.Append(string.Join(" OR ", predicates));
        }

        var result = new List<IReadOnlyDictionary<string, object?>>();

        using var conn = new SqlConnection(cs);
        conn.Open();
        using var cmd = new SqlCommand(sb.ToString(), conn);
        using var rdr = cmd.ExecuteReader();

        while (rdr.Read())
        {
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < rdr.FieldCount; i++)
                row[rdr.GetName(i)] = rdr.IsDBNull(i) ? null : rdr.GetValue(i);
            result.Add(row);
        }

        return result;
    }

    // ── DML generators ────────────────────────────────────────────────────────

    private static string BuildInsert(
        SqlTableInfo tableInfo,
        IReadOnlyDictionary<string, object?> row)
    {
        var allCols = tableInfo.PkColumns.Concat(tableInfo.DataColumns).ToList();
        var colNames = string.Join(", ", allCols.Select(c => c.QuotedName));
        var values   = string.Join(", ", allCols.Select(c =>
            row.TryGetValue(c.Name, out var v) ? ToSqlValue(v, c.TypeName) : "NULL"));

        return $"INSERT INTO {tableInfo.QualifiedName} ({colNames}) VALUES ({values});";
    }

    private static string BuildUpdate(
        SqlTableInfo tableInfo,
        IReadOnlyDictionary<string, object?> row)
    {
        var setClauses = tableInfo.DataColumns
            .Where(c => !c.IsIdentity && !c.IsComputed && !c.IsTimestamp)
            .Select(c => $"{c.QuotedName} = {(row.TryGetValue(c.Name, out var v) ? ToSqlValue(v, c.TypeName) : "NULL")}");

        var whereClauses = tableInfo.PkColumns
            .Select(c => $"{c.QuotedName} = {(row.TryGetValue(c.Name, out var v) ? ToSqlValue(v, c.TypeName) : "NULL")}");

        return $"UPDATE {tableInfo.QualifiedName} SET {string.Join(", ", setClauses)} " +
               $"WHERE {string.Join(" AND ", whereClauses)};";
    }

    private static string BuildDelete(SqlTableInfo tableInfo, RowDiff diff)
    {
        var whereClauses = tableInfo.PkColumns.Select(pkCol =>
        {
            var val = diff.PkValues.FirstOrDefault(p => p.Column == pkCol.Name).Value?.ToString() ?? "NULL";
            return $"{pkCol.QuotedName} = {SqlLiteral(val, pkCol.TypeName)}";
        });

        return $"DELETE FROM {tableInfo.QualifiedName} WHERE {string.Join(" AND ", whereClauses)};";
    }

    // ── SQL value helpers ─────────────────────────────────────────────────────

    private static string ToSqlValue(object? value, string typeName)
    {
        if (value is null or DBNull) return "NULL";

        return typeName.ToLower() switch
        {
            "int" or "bigint" or "smallint" or "tinyint" or "bit" =>
                Convert.ToString(value) ?? "NULL",
            "decimal" or "numeric" or "money" or "smallmoney" or "float" or "real" =>
                Convert.ToString(value) ?? "NULL",
            "datetime" or "datetime2" or "date" or "time" or "datetimeoffset" or "smalldatetime" =>
                $"'{Convert.ToString(value)}'",
            "uniqueidentifier" =>
                $"'{value}'",
            _ =>
                $"N'{EscapeString(Convert.ToString(value) ?? "")}'",
        };
    }

    private static string SqlLiteral(string value, string typeName)
    {
        if (value == "NULL") return "NULL";

        return typeName.ToLower() switch
        {
            "int" or "bigint" or "smallint" or "tinyint" or "bit"
                or "decimal" or "numeric" or "money" or "smallmoney"
                or "float" or "real" => value,
            _ => $"N'{EscapeString(value)}'",
        };
    }

    private static string EscapeString(string s) => s.Replace("'", "''");
}
