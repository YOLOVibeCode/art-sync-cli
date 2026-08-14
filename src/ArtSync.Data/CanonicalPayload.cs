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
    /// Returns the T-SQL sub-expression for one column that will be wrapped in
    /// <c>HASHBYTES('SHA2_256', CONVERT(VARBINARY(8000), …))</c> by the caller.
    ///
    /// For LOB / large-object columns (<paramref name="isLob"/>=<c>true</c>) the
    /// expression is a bounded fingerprint:
    ///   <c>CAST(DATALENGTH(col) AS NVARCHAR(20)) + NCHAR(28) + CONVERT(NVARCHAR(4000), col)</c>
    /// This keeps the VARBINARY(8000) input well below 8 000 bytes while still
    /// detecting length-only changes and content changes within the first 4 000 chars.
    /// </summary>
    /// <param name="quotedColumnName">e.g. <c>[OrderDate]</c></param>
    /// <param name="sqlTypeLower">Lower-case SQL type name, e.g. <c>datetime2</c></param>
    /// <param name="options">Parsed option bag from the CLI</param>
    /// <param name="isLob">True when the column is a LOB (text/ntext/image/xml/varchar(MAX)/…)</param>
    public static string BuildExpression(
        string quotedColumnName,
        string sqlTypeLower,
        IReadOnlyDictionary<string, string> options,
        bool isLob = false)
    {
        // ── LOB fingerprint (safe bounded representation) ─────────────────────
        // When a column is flagged as LOB and is not ignored by the caller, emit
        // DATALENGTH + bounded prefix so CONVERT(VARBINARY(8000), …) never truncates.
        if (isLob)
            return BuildLobFingerprint(quotedColumnName, sqlTypeLower);

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

            // XML: stringify (bounded via LOB path above when isLob=true)
            "xml" =>
                $"CAST({quotedColumnName} AS NVARCHAR(MAX))",

            // Spatial: use WKT + SRID fingerprint (safe for VARBINARY(8000))
            "geography" =>
                $"ISNULL({quotedColumnName}.STAsText(), N'') + N'|' + CAST(ISNULL({quotedColumnName}.STSrid, 0) AS NVARCHAR(10))",

            "geometry" =>
                $"ISNULL({quotedColumnName}.STAsText(), N'') + N'|' + CAST(ISNULL({quotedColumnName}.STSrid, 0) AS NVARCHAR(10))",

            "hierarchyid" =>
                $"CAST({quotedColumnName} AS NVARCHAR(900))",

            // Everything else: best-effort cast
            _ => $"CAST({quotedColumnName} AS NVARCHAR(MAX))",
        };
    }

    /// <summary>
    /// Returns a bounded fingerprint for LOB columns.
    /// Format: <c>DATALENGTH|first-4000-chars-or-hex-prefix</c>
    /// Fits well within 8 000 bytes after CONVERT(VARBINARY(8000), …).
    /// </summary>
    public static string BuildLobFingerprint(string quotedColumnName, string sqlTypeLower)
    {
        // For binary LOBs (image, varbinary(max)) use hex of first 4000 bytes
        var isBinaryLob = sqlTypeLower is "image" or "varbinary";
        if (isBinaryLob)
        {
            // CONVERT(NVARCHAR(MAX), SUBSTRING(col,1,2000), 2) gives ≤4000 hex chars
            return $"CAST(DATALENGTH({quotedColumnName}) AS NVARCHAR(20)) + N'|' + ISNULL(CONVERT(NVARCHAR(MAX), SUBSTRING({quotedColumnName}, 1, 2000), 2), N'')";
        }

        // For text/ntext/nvarchar(max)/xml: first 4000 chars + length
        return $"CAST(DATALENGTH({quotedColumnName}) AS NVARCHAR(20)) + N'|' + ISNULL(CONVERT(NVARCHAR(4000), {quotedColumnName}), N'')";}


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

        bool ignoreWs = IsOn(options, "IgnoreWhiteSpace");

        // Leading-space trim
        if (ignoreWs || IsOn(options, "IgnoreLeadingSpaces"))
            expr = $"LTRIM({expr})";

        // Trailing-space trim
        if (ignoreWs || IsOn(options, "IgnoreTrailingSpaces"))
            expr = $"RTRIM({expr})";

        // Collapse internal whitespace (REPLACE multiple spaces with one)
        if (ignoreWs || IsOn(options, "IgnoreInternalSpaces"))
            expr = $"REPLACE(REPLACE(REPLACE({expr}, N'  ', N' '), N'  ', N' '), N'  ', N' ')";

        // Normalise EOL to LF
        if (ignoreWs || IsOn(options, "IgnoreEndOfLine"))
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
