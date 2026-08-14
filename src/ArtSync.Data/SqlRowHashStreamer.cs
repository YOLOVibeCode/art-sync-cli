using ArtSync.Abstractions;
using Microsoft.Data.SqlClient;

namespace ArtSync.Data;

/// <summary>
/// Runs a server-side hash query for one table and streams
/// <c>(pkKey, rowHash)</c> pairs back to the caller.
///
/// The hash covers all data columns (excluding identity/rowversion/computed/LOB
/// unless options say otherwise). Full row data is NEVER fetched — only a
/// 32-byte SHA2_256 hash per row.
///
/// PK key format: PK column values cast to NVARCHAR, joined with FS (CHAR(28)).
/// Column separator inside the hash payload: FS (CHAR(28)).
/// NULL sentinel: empty string (NULL treated as '' — v1 limitation when
/// IsEmptyStringEqualsNull is not explicitly set to 'no').
/// </summary>
internal sealed class SqlRowHashStreamer
{
    // Field Separator (FS) — unlikely to appear in real data.
    private const string FsSql = "NCHAR(28)";

    public IEnumerable<(string PkKey, byte[] RowHash, IReadOnlyList<(string Col, string Val)> PkValues)>
        Stream(string connectionString, SqlTableInfo table, IReadOnlyDictionary<string, string> options)
    {
        var sql = BuildQuery(table, options);

        using var conn = new SqlConnection(connectionString);
        conn.Open();
        using var cmd = new SqlCommand(sql, conn)
        {
            CommandTimeout = 0  // no timeout for large tables
        };
        using var rdr = cmd.ExecuteReader();

        int pkCount = table.PkColumns.Count;

        while (rdr.Read())
        {
            var pkKey = rdr.GetString(0);
            var hash  = rdr.IsDBNull(1) ? Array.Empty<byte>() : (byte[])rdr[1];

            // Columns 2..n+1 are individual PK string values.
            var pkValues = new List<(string, string)>(pkCount);
            for (int i = 0; i < pkCount; i++)
                pkValues.Add((table.PkColumns[i].Name, rdr.IsDBNull(2 + i) ? "" : rdr.GetString(2 + i)));

            yield return (pkKey, hash, pkValues);
        }
    }

    // ── Query builder ─────────────────────────────────────────────────────────

    private static string BuildQuery(SqlTableInfo table, IReadOnlyDictionary<string, string> options)
    {
        bool ignoreIdentity  = IsOn(options, "IgnoreIdentityColumns", defaultOn: true);
        bool ignoreLob       = IsOn(options, "IgnoreLobColumns",       defaultOn: true);

        // ── PK key expression ─────────────────────────────────────────────────
        var pkKeyParts = table.PkColumns
            .Select(c => $"CAST({c.QuotedName} AS NVARCHAR(200))");
        var pkKeyExpr = string.Join($" + {FsSql} + ", pkKeyParts);

        // ── Hash payload columns ──────────────────────────────────────────────
        var hashCols = table.DataColumns
            .Where(c => !(ignoreIdentity && c.IsIdentity))
            .Where(c => !(ignoreLob      && c.IsLob))
            .ToList();

        string hashPayload;
        if (hashCols.Count == 0)
        {
            hashPayload = "N''";   // all columns excluded — always identical
        }
        else
        {
            var parts = hashCols.Select(c =>
            {
                var expr = CanonicalPayload.BuildExpression(c.QuotedName, c.TypeName.ToLower(), options);
                return c.IsNullable
                    ? $"COALESCE({expr}, N'')"
                    : expr;
            });
            hashPayload = string.Join($" + {FsSql} + ", parts);
        }

        // ── Individual PK columns for RowDiff construction ────────────────────
        var pkValueCols = table.PkColumns
            .Select(c => $"CAST({c.QuotedName} AS NVARCHAR(200)) AS {c.QuotedName}")
            .ToList();

        // ── ORDER BY ──────────────────────────────────────────────────────────
        var orderBy = string.Join(", ", table.PkColumns.Select(c => c.QuotedName));

        return $"""
            SELECT
                {pkKeyExpr} AS __PkKey,
                HASHBYTES('SHA2_256', CONVERT(VARBINARY(8000), {hashPayload})) AS __RowHash,
                {string.Join(",\n        ", pkValueCols)}
            FROM {table.QualifiedName}
            ORDER BY {orderBy}
            """;
    }

    private static bool IsOn(IReadOnlyDictionary<string, string> opts, string key, bool defaultOn)
    {
        if (!opts.TryGetValue(key, out var v)) return defaultOn;
        return v.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
               v.Equals("y",   StringComparison.OrdinalIgnoreCase) ||
               v.Equals("on",  StringComparison.OrdinalIgnoreCase) ||
               v.Equals("true",StringComparison.OrdinalIgnoreCase) ||
               v.Equals("t",   StringComparison.OrdinalIgnoreCase);
    }
}
