using System.Text;
using ArtSync.Abstractions;
using Microsoft.Data.SqlClient;

namespace ArtSync.Data;

/// <summary>
/// Fetches row payloads for differing keys from the source and generates
/// T-SQL INSERT / UPDATE / DELETE statements.
///
/// Global DML order (SPEC §10.2 step 8):
///   1. Optional NOCHECK FK / DISABLE TRIGGER wrappers
///   2. DELETE children-first
///   3. UPDATE
///   4. INSERT parents-first (IDENTITY_INSERT when needed)
///   5. Re-enable triggers and WITH CHECK CHECK CONSTRAINT
/// </summary>
internal sealed class SqlDataScripter
{
    private readonly SqlMetadataReader _meta = new();
    private const int FetchBatchSize = 200;

    public string Script(
        string sourceCs,
        string targetCs,
        IReadOnlyList<RowDiff> diffs,
        IReadOnlyDictionary<string, string> options)
    {
        if (diffs.Count == 0) return "-- No data differences.";

        var sb = new StringBuilder();
        if (!SqlOptionFlags.IsOn(options, "ExcludeComments", defaultOn: false))
        {
            sb.AppendLine("-- ArtSync data sync script");
            sb.AppendLine($"-- Generated: {DateTime.UtcNow:O}");
            sb.AppendLine();
        }

        if (SqlOptionFlags.IsOn(options, "IncludeUseDatabase", defaultOn: false))
        {
            var catalog = new SqlConnectionStringBuilder(targetCs).InitialCatalog;
            if (!string.IsNullOrEmpty(catalog))
            {
                sb.AppendLine($"USE {QuoteIdent(catalog)};");
                sb.AppendLine("GO");
                sb.AppendLine();
            }
        }

        bool includeViews = SqlOptionFlags.IsOn(options, "CompareViews", defaultOn: false);
        var srcTables = _meta.ReadTables(sourceCs, includeTables: true, includeViews)
            .ToDictionary(t => t.QualifiedName, StringComparer.OrdinalIgnoreCase);
        var tgtTables = _meta.ReadTables(targetCs, includeTables: true, includeViews)
            .ToDictionary(t => t.QualifiedName, StringComparer.OrdinalIgnoreCase);

        var fks = SqlFkGraph.Read(targetCs);
        var tableNames = diffs.Select(d => d.TableName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        string ResolveTgt(string sourceName)
        {
            if (tgtTables.ContainsKey(sourceName)) return sourceName;
            var key = SqlDataCompare.MapKey(sourceName, options);
            return tgtTables.Values.FirstOrDefault(t =>
                       string.Equals(SqlDataCompare.MapKey(t.QualifiedName, options), key,
                           StringComparison.OrdinalIgnoreCase))
                   ?.QualifiedName
                   ?? sourceName;
        }

        var insertOrder = SqlFkGraph.ParentsFirst(tableNames, fks);
        var deleteOrder = SqlFkGraph.ChildrenFirst(tableNames, fks);

        bool nofk  = SqlOptionFlags.IsOn(options, "DisableForeignKeys", defaultOn: true);
        bool nodml = SqlOptionFlags.IsOn(options, "DisableDmlTriggers", defaultOn: true);
        bool noddl = SqlOptionFlags.IsOn(options, "DisableDdlTriggers", defaultOn: false);

        var wrapTables = SqlFkGraph.InvolvedTables(tableNames, fks)
            .Select(ResolveTgt)
            .Where(tgtTables.ContainsKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (noddl)
        {
            sb.AppendLine("DISABLE TRIGGER ALL ON DATABASE;");
            sb.AppendLine();
        }

        if (nofk)
        {
            foreach (var t in wrapTables)
                sb.AppendLine($"ALTER TABLE {t} NOCHECK CONSTRAINT ALL;");
            sb.AppendLine();
        }

        if (nodml)
        {
            foreach (var t in wrapTables)
                sb.AppendLine($"DISABLE TRIGGER ALL ON {t};");
            sb.AppendLine();
        }

        var grouped = diffs.GroupBy(d => d.TableName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var tableName in deleteOrder)
        {
            if (!grouped.TryGetValue(tableName, out var group)) continue;
            if (!srcTables.TryGetValue(tableName, out var tableInfo)
                && !tgtTables.TryGetValue(tableName, out tableInfo))
            {
                sb.AppendLine($"-- WARNING: table {tableName} not found; skipped.");
                continue;
            }

            var deletes = group.Where(d => d.Kind == RowDiffKind.OnlyInTarget).ToList();
            if (deletes.Count == 0) continue;
            tableInfo = AlignWithTarget(tableInfo, tgtTables, options);
            foreach (var diff in deletes)
                sb.AppendLine(BuildDelete(tableInfo, diff, options));
            sb.AppendLine();
        }

        foreach (var tableName in insertOrder)
        {
            if (!grouped.TryGetValue(tableName, out var group)) continue;
            if (!srcTables.TryGetValue(tableName, out var tableInfo))
            {
                sb.AppendLine($"-- WARNING: table {tableName} not found in source; skipped.");
                continue;
            }

            tableInfo = AlignWithTarget(tableInfo, tgtTables, options);
            var updates = group.Where(d => d.Kind == RowDiffKind.Different).ToList();
            if (updates.Count == 0) continue;

            var rows = FetchRows(sourceCs, tableInfo, updates);
            foreach (var row in rows)
                sb.AppendLine(BuildUpdate(tableInfo, row, options));
            sb.AppendLine();
        }

        foreach (var tableName in insertOrder)
        {
            if (!grouped.TryGetValue(tableName, out var group)) continue;
            if (!srcTables.TryGetValue(tableName, out var tableInfo))
            {
                sb.AppendLine($"-- WARNING: table {tableName} not found in source; skipped.");
                continue;
            }

            tableInfo = AlignWithTarget(tableInfo, tgtTables, options);
            var inserts = group.Where(d => d.Kind == RowDiffKind.OnlyInSource).ToList();
            if (inserts.Count == 0) continue;

            bool hasIdentity = tableInfo.PkColumns.Any(c => c.IsIdentity)
                            || tableInfo.DataColumns.Any(c => c.IsIdentity);

            var rows = FetchRows(sourceCs, tableInfo, inserts);
            bool bulkInsert = SqlOptionFlags.IsOn(options, "BulkInsert", defaultOn: false);
            var tableRef = TableRef(tableInfo, options);
            if (hasIdentity)
                sb.AppendLine($"SET IDENTITY_INSERT {tableRef} ON;");
            if (bulkInsert)
                AppendBulkInserts(sb, tableInfo, rows, options);
            else
                foreach (var row in rows)
                    sb.AppendLine(BuildInsert(tableInfo, row, options));
            if (hasIdentity)
                sb.AppendLine($"SET IDENTITY_INSERT {tableRef} OFF;");
            sb.AppendLine();
        }

        if (nodml)
        {
            foreach (var t in wrapTables)
                sb.AppendLine($"ENABLE TRIGGER ALL ON {t};");
            sb.AppendLine();
        }

        if (nofk)
        {
            foreach (var t in wrapTables)
                sb.AppendLine($"ALTER TABLE {t} WITH CHECK CHECK CONSTRAINT ALL;");
        }

        if (noddl)
        {
            sb.AppendLine();
            sb.AppendLine("ENABLE TRIGGER ALL ON DATABASE;");
        }

        // ── ReseedIdentityColumns ─────────────────────────────────────────────
        // After apply, reseed IDENTITY columns so later inserts don't collide with
        // synced IDs.  Default on after any insert that touches identity tables.
        bool reseed = SqlOptionFlags.IsOn(options, "ReseedIdentityColumns", defaultOn: true);
        if (reseed)
        {
            var touchedIdentityTables = tableNames
                .Where(t => srcTables.TryGetValue(t, out var ti) &&
                            (ti.PkColumns.Any(c => c.IsIdentity) || ti.DataColumns.Any(c => c.IsIdentity)))
                .ToList();

            if (touchedIdentityTables.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("-- Reseed identity columns to source maximums");
                int reseedIdx = 0;
                foreach (var t in touchedIdentityTables)
                {
                    if (!srcTables.TryGetValue(t, out var ti)) continue;
                    var idCol = ti.PkColumns.FirstOrDefault(c => c.IsIdentity)
                             ?? ti.DataColumns.FirstOrDefault(c => c.IsIdentity);
                    if (idCol is null) continue;
                    // DBCC CHECKIDENT requires a literal value; use a variable.
                    var varName = $"@__reseed_{reseedIdx++}";
                    var applyName = TableRef(ti with { TargetQualifiedName = ResolveTgt(t) }, options);
                    var bareTable = applyName.Replace("[", "").Replace("]", "");
                    sb.AppendLine($"DECLARE {varName} BIGINT = (SELECT ISNULL(MAX({idCol.QuotedName}), 0) FROM {applyName});");
                    sb.AppendLine($"DBCC CHECKIDENT ('{bareTable}', RESEED, {varName});");
                }
            }
        }

        return sb.ToString();
    }

    // ── Align source metadata with columns that exist on the target ───────────

    private static SqlTableInfo AlignWithTarget(
        SqlTableInfo source,
        IReadOnlyDictionary<string, SqlTableInfo> tgtTables,
        IReadOnlyDictionary<string, string> options)
    {
        var data = source.DataColumns
            .Where(c => !SqlNameMask.IsColumnIgnored(c.Name, options))
            .Where(c => !IsTemporalSysColumn(c.Name, options))
            .ToList();

        SqlTableInfo? tgt = null;
        if (!tgtTables.TryGetValue(source.QualifiedName, out tgt))
        {
            var key = SqlDataCompare.MapKey(source.QualifiedName, options);
            tgt = tgtTables.Values.FirstOrDefault(t =>
                string.Equals(SqlDataCompare.MapKey(t.QualifiedName, options), key,
                    StringComparison.OrdinalIgnoreCase));
        }

        if (tgt is null)
            return source with { DataColumns = data };

        var tgtNames = tgt.PkColumns.Concat(tgt.DataColumns)
            .Select(c => c.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        data = data.Where(c => tgtNames.Contains(c.Name)).ToList();
        return source with { DataColumns = data, TargetQualifiedName = tgt.QualifiedName };
    }

    internal static bool IsTemporalSysColumn(string name, IReadOnlyDictionary<string, string> options)
    {
        if (!SqlOptionFlags.IsOn(options, "IgnoreTemporalTableSysColumns", defaultOn: true))
            return false;
        return name.Equals("ValidFrom", StringComparison.OrdinalIgnoreCase)
            || name.Equals("ValidTo", StringComparison.OrdinalIgnoreCase)
            || name.Equals("SysStartTime", StringComparison.OrdinalIgnoreCase)
            || name.Equals("SysEndTime", StringComparison.OrdinalIgnoreCase);
    }

    private static string TableRef(SqlTableInfo table, IReadOnlyDictionary<string, string> options)
    {
        var name = table.ApplyName;
        if (SqlOptionFlags.IsOn(options, "UseSchemaNamePrefix", defaultOn: true))
            return name;
        var dot = name.LastIndexOf('.');
        return dot < 0 ? name : name[(dot + 1)..];
    }

    private static string QuoteIdent(string name)
        => "[" + name.Replace("]", "]]") + "]";

    // ── Row fetching ──────────────────────────────────────────────────────────

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> FetchRows(
        string cs,
        SqlTableInfo tableInfo,
        IReadOnlyList<RowDiff> diffs)
    {
        if (diffs.Count == 0) return [];

        var result = new List<IReadOnlyDictionary<string, object?>>();
        for (int i = 0; i < diffs.Count; i += FetchBatchSize)
        {
            var batch = diffs.Skip(i).Take(FetchBatchSize).ToList();
            result.AddRange(FetchRowBatch(cs, tableInfo, batch));
        }
        return result;
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> FetchRowBatch(
        string cs,
        SqlTableInfo tableInfo,
        IReadOnlyList<RowDiff> diffs)
    {
        var selectCols = tableInfo.PkColumns.Concat(tableInfo.DataColumns)
            .Select(SelectExpression);
        var sb = new StringBuilder();
        sb.Append($"SELECT {string.Join(", ", selectCols)} FROM {tableInfo.QualifiedName} WHERE ");

        if (tableInfo.PkColumns.Count == 1)
        {
            var pkCol = tableInfo.PkColumns[0];
            var values = diffs.Select(d =>
            {
                var val = d.PkValues.FirstOrDefault(p => p.Column == pkCol.Name).Value;
                return SqlValueFormatter.Format(val, pkCol.TypeName);
            });
            sb.Append($"{pkCol.QuotedName} IN ({string.Join(", ", values)})");
        }
        else
        {
            var predicates = diffs.Select(d =>
            {
                var clauses = tableInfo.PkColumns.Select(pkCol =>
                {
                    var val = d.PkValues.FirstOrDefault(p => p.Column == pkCol.Name).Value;
                    return $"{pkCol.QuotedName} = {SqlValueFormatter.Format(val, pkCol.TypeName)}";
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
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < rdr.FieldCount; i++)
                row[rdr.GetName(i)] = rdr.IsDBNull(i) ? null : rdr.GetValue(i);
            result.Add(row);
        }
        return result;
    }

    /// <summary>
    /// SELECT list expression: spatial types via Serialize(), hierarchyid as nvarchar,
    /// everything else as the quoted column name.
    /// </summary>
    private static string SelectExpression(SqlColumnInfo c)
    {
        var t = c.TypeName.ToLowerInvariant();
        return t switch
        {
            "geography" or "geometry" => $"{c.QuotedName}.Serialize() AS {c.QuotedName}",
            "hierarchyid" => $"CAST({c.QuotedName} AS NVARCHAR(900)) AS {c.QuotedName}",
            _ => c.QuotedName,
        };
    }

    // ── DML generators ────────────────────────────────────────────────────────

    private const int BulkBatchSize = 100;
    private const int MaxLobBytes = 1_048_576;

    private static void AppendBulkInserts(
        StringBuilder sb,
        SqlTableInfo tableInfo,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        IReadOnlyDictionary<string, string> options)
    {
        if (rows.Count == 0) return;

        var allCols = tableInfo.PkColumns.Concat(tableInfo.DataColumns)
            .Where(c => !c.IsComputed && !c.IsTimestamp)
            .Where(c => !SqlNameMask.IsColumnIgnored(c.Name, options))
            .ToList();

        var colNames = string.Join(", ", allCols.Select(c => c.QuotedName));
        var tableRef = TableRef(tableInfo, options);

        for (int i = 0; i < rows.Count; i += BulkBatchSize)
        {
            var batch = rows.Skip(i).Take(BulkBatchSize).ToList();
            sb.AppendLine($"INSERT INTO {tableRef} ({colNames}) VALUES");
            for (int j = 0; j < batch.Count; j++)
            {
                var values = string.Join(", ", allCols.Select(c => FormatCol(c, batch[j])));
                var comma = j < batch.Count - 1 ? "," : ";";
                sb.AppendLine($"    ({values}){comma}");
            }
        }
    }

    private static string BuildInsert(
        SqlTableInfo tableInfo,
        IReadOnlyDictionary<string, object?> row,
        IReadOnlyDictionary<string, string> options)
    {
        var allCols = tableInfo.PkColumns.Concat(tableInfo.DataColumns)
            .Where(c => !c.IsComputed && !c.IsTimestamp)
            .Where(c => !SqlNameMask.IsColumnIgnored(c.Name, options))
            .ToList();
        var colNames = string.Join(", ", allCols.Select(c => c.QuotedName));
        var values   = string.Join(", ", allCols.Select(c => FormatCol(c, row)));

        return $"INSERT INTO {TableRef(tableInfo, options)} ({colNames}) VALUES ({values});";
    }

    private static string BuildUpdate(
        SqlTableInfo tableInfo,
        IReadOnlyDictionary<string, object?> row,
        IReadOnlyDictionary<string, string> options)
    {
        bool ignoreRowguid = SqlOptionFlags.IsOn(options, "IgnoreRowguidColumns", defaultOn: true);

        var setClauses = tableInfo.DataColumns
            .Where(c => !c.IsIdentity && !c.IsComputed && !c.IsTimestamp)
            .Where(c => !(ignoreRowguid && c.IsRowguid))
            .Where(c => !SqlNameMask.IsColumnIgnored(c.Name, options))
            .Select(c => $"{c.QuotedName} = {FormatCol(c, row)}");

        var setList = setClauses.ToList();
        var tableRef = TableRef(tableInfo, options);
        if (setList.Count == 0)
            return $"-- skip UPDATE {tableRef}: no writable columns";

        var whereClauses = tableInfo.PkColumns
            .Select(c => $"{c.QuotedName} = {FormatCol(c, row)}");

        return $"UPDATE {tableRef} SET {string.Join(", ", setList)} " +
               $"WHERE {string.Join(" AND ", whereClauses)};";
    }

    private static string BuildDelete(
        SqlTableInfo tableInfo,
        RowDiff diff,
        IReadOnlyDictionary<string, string> options)
    {
        var whereClauses = tableInfo.PkColumns.Select(pkCol =>
        {
            var val = diff.PkValues.FirstOrDefault(p => p.Column == pkCol.Name).Value;
            return $"{pkCol.QuotedName} = {SqlValueFormatter.Format(val, pkCol.TypeName)}";
        });

        return $"DELETE FROM {TableRef(tableInfo, options)} WHERE {string.Join(" AND ", whereClauses)};";
    }

    private static string FormatCol(SqlColumnInfo c, IReadOnlyDictionary<string, object?> row)
    {
        if (!row.TryGetValue(c.Name, out var v) || v is null) return "NULL";
        if (c.IsLob) GuardLobSize(v);
        return SqlValueFormatter.Format(v, c.TypeName);
    }

    /// <summary>
    /// SPEC §10.2: LOB values larger than 1 MB require /fspath (unsupported in v1).
    /// </summary>
    internal static void GuardLobSize(object value)
    {
        int bytes = value switch
        {
            byte[] b => b.Length,
            string s => System.Text.Encoding.Unicode.GetByteCount(s),
            _ => 0,
        };
        if (bytes > MaxLobBytes)
            throw new InvalidOperationException(
                $"LOB value exceeds 1 MB ({bytes} bytes). " +
                "FileStoragePath (/fspath) is not supported in v1.");
    }
}
