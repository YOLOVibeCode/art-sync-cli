namespace ArtSync.Data;

/// <summary>
/// Builds the T-SQL expression that is passed to <c>HASHBYTES('SHA2_256', …)</c>
/// for a single column, according to the canonical-payload rules in SPEC §10.2.
///
/// The resulting expression is embedded into a server-side hash query so that
/// full row data is NEVER fetched to the CLI host for non-differing rows.
/// </summary>
public static class CanonicalPayload
{
    /// <summary>
    /// Returns the T-SQL sub-expression for one column that can be concatenated
    /// with <c>'|'</c> separators and hashed server-side.
    /// </summary>
    /// <param name="quotedColumnName">e.g. <c>[OrderDate]</c></param>
    /// <param name="sqlTypeLower">Lower-case SQL type name, e.g. <c>datetime2</c></param>
    /// <param name="options">Parsed option bag from the CLI</param>
    public static string BuildExpression(
        string quotedColumnName,
        string sqlTypeLower,
        IReadOnlyDictionary<string, string> options)
    {
        // ── Columns that must be excluded from the hash ───────────────────────
        // Callers are responsible for filtering out identity/rowversion/computed/
        // LOB/rowguid/temporal sys columns before calling this method.

        // ── Normalise by type ─────────────────────────────────────────────────
        return sqlTypeLower switch
        {
            // Date-only comparison (strip time part)
            "datetime" or "datetime2" or "smalldatetime" or "date" or "datetimeoffset"
                when IsIgnoreTime(options) =>
                $"CONVERT(NVARCHAR(50), CAST({quotedColumnName} AS DATE), 126)",

            // Full datetime ISO
            "datetime" or "datetime2" or "smalldatetime" or "datetimeoffset" =>
                $"CONVERT(NVARCHAR(50), {quotedColumnName}, 126)",

            "date" =>
                $"CONVERT(NVARCHAR(20), {quotedColumnName}, 126)",

            "time" =>
                $"CONVERT(NVARCHAR(20), {quotedColumnName}, 114)",

            // Float: round to avoid platform-precision diffs
            "float" or "real" when IsRoundFloat(options) =>
                $"CAST(ROUND({quotedColumnName}, 10) AS NVARCHAR(50))",

            "float" or "real" =>
                $"CAST({quotedColumnName} AS NVARCHAR(50))",

            // Binary types: hex-encode so HASHBYTES sees printable chars
            "binary" or "varbinary" or "rowversion" or "timestamp" =>
                $"CONVERT(NVARCHAR(MAX), {quotedColumnName}, 2)",

            // Bit: normalise to '0'/'1'
            "bit" =>
                $"CAST({quotedColumnName} AS NVARCHAR(5))",

            // Uniqueidentifier: uppercase normalise
            "uniqueidentifier" =>
                $"UPPER(CAST({quotedColumnName} AS NVARCHAR(40)))",

            // String types: apply whitespace / case normalisation
            "char" or "nchar" or "varchar" or "nvarchar" or "text" or "ntext" =>
                BuildStringExpression(quotedColumnName, sqlTypeLower, options),

            // Numeric / money: cast to string preserving full precision
            "decimal" or "numeric" or "money" or "smallmoney" =>
                $"CAST({quotedColumnName} AS NVARCHAR(50))",

            // Integer types: straight cast
            "int" or "bigint" or "smallint" or "tinyint" =>
                $"CAST({quotedColumnName} AS NVARCHAR(20))",

            // XML: stringify
            "xml" =>
                $"CAST({quotedColumnName} AS NVARCHAR(MAX))",

            // Everything else: best-effort cast
            _ => $"CAST({quotedColumnName} AS NVARCHAR(MAX))",
        };
    }

    /// <summary>
    /// Returns the NULL-safe COALESCE wrapper: if the column is NULL, use a
    /// distinguishable sentinel so NULL ≠ empty string unless
    /// <c>IsEmptyStringEqualsNull</c> is on.
    /// </summary>
    public static string NullSafe(
        string expression,
        bool isNullable,
        IReadOnlyDictionary<string, string> options)
    {
        if (!isNullable) return expression;

        var nullEqualsEmpty = IsOn(options, "IsEmptyStringEqualsNull");
        if (nullEqualsEmpty)
        {
            // NULL and '' are treated as equal; use empty string for both.
            return $"COALESCE({expression}, N'')";
        }
        else
        {
            // NULL is distinct; use a sentinel that cannot appear in real data.
            return $"COALESCE({expression}, N'\x01NULL\x01')";
        }
    }

    // ── Option helpers ────────────────────────────────────────────────────────

    private static bool IsIgnoreTime(IReadOnlyDictionary<string, string> opts)
        => IsOn(opts, "IsIgnoreTime");

    private static bool IsRoundFloat(IReadOnlyDictionary<string, string> opts)
        => IsOn(opts, "RoundFloatTypes");

    private static bool IsOn(IReadOnlyDictionary<string, string> opts, string key)
    {
        if (!opts.TryGetValue(key, out var v)) return false;
        return v.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
               v.Equals("y",   StringComparison.OrdinalIgnoreCase) ||
               v.Equals("on",  StringComparison.OrdinalIgnoreCase) ||
               v.Equals("true",StringComparison.OrdinalIgnoreCase) ||
               v.Equals("t",   StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildStringExpression(
        string quotedCol,
        string sqlTypeLower,
        IReadOnlyDictionary<string, string> options)
    {
        var expr = quotedCol;

        // Leading-space trim
        if (IsOn(options, "IgnoreLeadingSpaces"))
            expr = $"LTRIM({expr})";

        // Trailing-space trim
        if (IsOn(options, "IgnoreTrailingSpaces"))
            expr = $"RTRIM({expr})";

        // Collapse internal whitespace (REPLACE multiple spaces with one)
        if (IsOn(options, "IgnoreInternalSpaces"))
            expr = $"REPLACE(REPLACE(REPLACE({expr}, N'  ', N' '), N'  ', N' '), N'  ', N' ')";

        // Normalise EOL to LF
        if (IsOn(options, "IgnoreEndOfLine"))
            expr = $"REPLACE(REPLACE({expr}, N'\r\n', N'\n'), N'\r', N'\n')";

        // Case: UPPER for case-insensitive comparison
        if (IsOn(options, "IgnoreCase"))
            expr = $"UPPER({expr})";

        // For NVARCHAR casts not strictly required (already string), but ensure N prefix
        // for non-unicode types.
        if (sqlTypeLower is "char" or "varchar" or "text")
            expr = $"CAST({expr} AS NVARCHAR(MAX))";

        return expr;
    }
}
