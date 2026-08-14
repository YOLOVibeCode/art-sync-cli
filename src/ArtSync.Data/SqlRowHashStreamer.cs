using ArtSync.Abstractions;
using Microsoft.Data.SqlClient;

namespace ArtSync.Data;

/// <summary>
/// Runs a server-side hash query for one table and streams
/// <c>(pkKey, rowHash, pkValues)</c> pairs back to the caller.
///
/// PK values are returned as native CLR types so DML literals round-trip.
/// The hash payload uses <see cref="CanonicalPayload.NullSafe"/> so NULL is
/// distinct from empty string unless <c>IsEmptyStringEqualsNull</c> is on.
/// </summary>
internal sealed class SqlRowHashStreamer
{
    private const string FsSql = "NCHAR(28)";

    public IEnumerable<(string PkKey, byte[] RowHash, IReadOnlyList<(string Col, object? Val)> PkValues)>
        Stream(string connectionString, SqlTableInfo table, IReadOnlyDictionary<string, string> options)
    {
        var sql = BuildQuery(table, options);

        using var conn = new SqlConnection(connectionString);
        conn.Open();
        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 0 };
        using var rdr = cmd.ExecuteReader();

        int pkCount = table.PkColumns.Count;

        while (rdr.Read())
        {
            var pkKey = rdr.IsDBNull(0) ? "" : rdr.GetString(0);
            var hash  = rdr.IsDBNull(1) ? Array.Empty<byte>() : (byte[])rdr[1];

            var pkValues = new List<(string, object?)>(pkCount);
            for (int i = 0; i < pkCount; i++)
                pkValues.Add((table.PkColumns[i].Name, rdr.IsDBNull(2 + i) ? null : rdr.GetValue(2 + i)));

            yield return (pkKey, hash, pkValues);
        }
    }

    private static string BuildQuery(SqlTableInfo table, IReadOnlyDictionary<string, string> options)
    {
        bool ignoreIdentity = SqlOptionFlags.IsOn(options, "IgnoreIdentityColumns", defaultOn: true);
        bool ignoreLob      = SqlOptionFlags.IsOn(options, "IgnoreLobColumns",       defaultOn: true);
        bool ignoreRowguid  = SqlOptionFlags.IsOn(options, "IgnoreRowguidColumns",   defaultOn: true);

        var pkKeyParts = table.PkColumns.Select(c =>
            CanonicalPayload.NullSafe(
                CanonicalPayload.BuildExpression(c.QuotedName, c.TypeName.ToLowerInvariant(), options),
                c.IsNullable,
                options));
        var pkKeyExpr = string.Join($" + {FsSql} + ", pkKeyParts);

        var hashCols = table.DataColumns
            .Where(c => !(ignoreIdentity && c.IsIdentity))
            .Where(c => !(ignoreLob      && c.IsLob))
            .Where(c => !(ignoreRowguid  && c.IsRowguid))
            .Where(c => !SqlNameMask.IsColumnIgnored(c.Name, options))
            .Where(c => !SqlDataScripter.IsTemporalSysColumn(c.Name, options))
            .ToList();

        // Build per-column hash segments. Each column's canonical expression is
        // hashed independently to 32 bytes (SHA2_256), then those 32-byte digests
        // are concatenated and hashed again. This avoids the VARBINARY(8000)
        // truncation that occurs when a single wide row payload exceeds 8000 bytes.
        string rowHashExpr;
        if (hashCols.Count == 0)
        {
            rowHashExpr = "HASHBYTES('SHA2_256', CONVERT(VARBINARY(8000), N''))";
        }
        else
        {
            var perColHashes = hashCols.Select(c =>
            {
                var expr = CanonicalPayload.NullSafe(
                    CanonicalPayload.BuildExpression(c.QuotedName, c.TypeName.ToLowerInvariant(), options, isLob: c.IsLob),
                    c.IsNullable,
                    options);
                // Each column → 32-byte digest; LOB-safe because BuildExpression
                // already returns a bounded fingerprint for LOB types.
                return $"HASHBYTES('SHA2_256', CONVERT(VARBINARY(8000), {expr}))";
            });
            var combined = string.Join("\n      + ", perColHashes);
            rowHashExpr = $"HASHBYTES('SHA2_256',\n      {combined})";
        }

        var pkValueCols = table.PkColumns.Select(c => c.QuotedName);
        var orderBy = string.Join(", ", table.PkColumns.Select(c => c.QuotedName));

        return $"""
            SELECT
                {pkKeyExpr} AS __PkKey,
                {rowHashExpr} AS __RowHash,
                {string.Join(",\n        ", pkValueCols)}
            FROM {table.QualifiedName}
            ORDER BY {orderBy}
            """;
    }
}
