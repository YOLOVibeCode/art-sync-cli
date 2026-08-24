using System.Globalization;

namespace ArtSync.Data;

/// <summary>
/// Converts a .NET value (as returned by <c>SqlDataReader.GetValue()</c>) to a
/// T-SQL literal that can be embedded in a generated sync script.
///
/// SQL Server type name → expected .NET CLR type:
/// <list type="table">
///   <item><term>int / bigint / smallint / tinyint</term><description>Int32, Int64, Int16, Byte</description></item>
///   <item><term>bit</term><description>Boolean</description></item>
///   <item><term>decimal / numeric / money / smallmoney</term><description>Decimal</description></item>
///   <item><term>float</term><description>Double</description></item>
///   <item><term>real</term><description>Single</description></item>
///   <item><term>datetime / datetime2 / smalldatetime</term><description>DateTime</description></item>
///   <item><term>date</term><description>DateTime (time part midnight)</description></item>
///   <item><term>time</term><description>TimeSpan</description></item>
///   <item><term>datetimeoffset</term><description>DateTimeOffset</description></item>
///   <item><term>char / varchar / nchar / nvarchar / text / ntext / xml</term><description>String</description></item>
///   <item><term>binary / varbinary / image</term><description>byte[]</description></item>
///   <item><term>uniqueidentifier</term><description>Guid</description></item>
///   <item><term>geography / geometry</term><description>byte[] (binary Serialize() form via SELECT col.Serialize())</description></item>
///   <item><term>hierarchyid</term><description>String from CAST(col AS NVARCHAR(900))</description></item>
/// </list>
/// </summary>
public static class SqlValueFormatter
{
    /// <summary>
    /// Returns the T-SQL literal for <paramref name="value"/> given its SQL type name.
    /// Returns <c>NULL</c> for null / DBNull inputs.
    /// </summary>
    public static string Format(object? value, string sqlTypeName)
    {
        if (value is null or DBNull) return "NULL";

        return sqlTypeName.ToLowerInvariant() switch
        {
            // ── Integers — unquoted ───────────────────────────────────────────
            "int"      => Numeric(value),
            "bigint"   => Numeric(value),
            "smallint" => Numeric(value),
            "tinyint"  => Numeric(value),

            // ── Bit — unquoted 1 / 0 ─────────────────────────────────────────
            "bit" => value is bool b ? (b ? "1" : "0")
                     : Convert.ToInt32(value).ToString(CultureInfo.InvariantCulture),

            // ── Exact numerics — unquoted, invariant culture ──────────────────
            "decimal"    => Decimal(value),
            "numeric"    => Decimal(value),
            "money"      => Decimal(value),
            "smallmoney" => Decimal(value),

            // ── Approximate numerics — unquoted, round-trip format ────────────
            "float" => value is double d
                ? d.ToString("R", CultureInfo.InvariantCulture)
                : Convert.ToDouble(value).ToString("R", CultureInfo.InvariantCulture),

            "real" => value is float f
                ? f.ToString("R", CultureInfo.InvariantCulture)
                : Convert.ToSingle(value).ToString("R", CultureInfo.InvariantCulture),

            // ── DateTime types — quoted ISO 8601 ─────────────────────────────
            // datetime:      3ms precision  → max 3 fractional digits
            // datetime2:     100ns precision → 7 fractional digits
            // smalldatetime: 1-minute precision → NO fractional seconds accepted in literals
            "datetime" =>
                $"'{ToDateTime(value).ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)}'",

            "datetime2" =>
                $"'{ToDateTime(value).ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture)}'",

            "smalldatetime" =>
                $"'{ToDateTime(value).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}'",

            "date" =>
                $"'{ToDateTime(value):yyyy-MM-dd}'",

            "time" =>
                value is TimeSpan ts
                    ? $"'{ts:hh\\:mm\\:ss\\.fffffff}'"
                    : $"'{value}'",

            "datetimeoffset" =>
                value is DateTimeOffset dto
                    ? $"'{dto.ToString("yyyy-MM-dd HH:mm:ss.fffffff zzz", CultureInfo.InvariantCulture)}'"
                    : $"'{value}'",

            // ── Binary types — 0xHEX unquoted ────────────────────────────────
            "binary" or "varbinary" or "image" =>
                value is byte[] ba
                    ? (ba.Length == 0 ? "0x" : $"0x{Convert.ToHexString(ba)}")
                    : "NULL",

            // ── Spatial types — geography::Deserialize / geometry::Deserialize ──
            // FetchRows should SELECT col.Serialize() AS col_Bytes; the reader
            // returns byte[].  Fall back to N'NULL' if not binary.
            "geography" =>
                value is byte[] gba
                    ? $"geography::Deserialize(0x{Convert.ToHexString(gba)})"
                    : "NULL",

            "geometry" =>
                value is byte[] gmba
                    ? $"geometry::Deserialize(0x{Convert.ToHexString(gmba)})"
                    : "NULL",

            // ── hierarchyid — CAST string literal ────────────────────────────
            "hierarchyid" =>
                $"CAST(N'{EscapeString(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "")}' AS hierarchyid)",

            // ── GUID — quoted, braces stripped ───────────────────────────────
            "uniqueidentifier" =>
                value is Guid g
                    ? $"'{g:D}'"    // "D" = 32 hex digits with hyphens, no braces
                    : $"'{EscapeString(value.ToString()!)}'",

            // ── sql_variant — typed CONVERT so int 42 does not become N'42' ───
            "sql_variant" => FormatSqlVariant(value, baseType: null),

            // ── String / XML — N'...' with escaped single quotes ──────────────
            _ =>
                $"N'{EscapeString(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "")}'",
        };
    }

    /// <summary>
    /// Emits a T-SQL literal that preserves the sql_variant base type.
    /// <c>INSERT ... VALUES (N'42')</c> stores nvarchar, not int.
    /// </summary>
    internal static string FormatSqlVariant(object value, string? baseType)
    {
        var bt = string.IsNullOrWhiteSpace(baseType)
            ? InferSqlVariantBaseType(value)
            : baseType.Trim().ToLowerInvariant();

        return bt switch
        {
            "bit" => value is bool b ? (b ? "CONVERT(bit, 1)" : "CONVERT(bit, 0)")
                     : $"CONVERT(bit, {Convert.ToInt32(value, CultureInfo.InvariantCulture)})",
            "tinyint" => $"CONVERT(tinyint, {Convert.ToByte(value, CultureInfo.InvariantCulture)})",
            "smallint" => $"CONVERT(smallint, {Convert.ToInt16(value, CultureInfo.InvariantCulture)})",
            "int" => $"CONVERT(int, {Convert.ToInt32(value, CultureInfo.InvariantCulture)})",
            "bigint" => $"CONVERT(bigint, {Convert.ToInt64(value, CultureInfo.InvariantCulture)})",
            "decimal" or "numeric" =>
                $"CONVERT(decimal(38, 18), {Convert.ToDecimal(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)})",
            "money" or "smallmoney" =>
                $"CONVERT({bt}, {Convert.ToDecimal(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)})",
            "float" => $"CONVERT(float, {Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture)})",
            "real" => $"CONVERT(real, {Convert.ToSingle(value, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture)})",
            "datetime" or "smalldatetime" or "datetime2" or "date" or "time" or "datetimeoffset" =>
                $"CONVERT({bt}, {Format(value, bt)})",
            "uniqueidentifier" => $"CONVERT(uniqueidentifier, {Format(value, "uniqueidentifier")})",
            "binary" or "varbinary" =>
                $"CONVERT(varbinary(8000), {Format(value, "varbinary")})",
            "varchar" or "char" =>
                $"CONVERT({bt}(8000), N'{EscapeString(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "")}')",
            _ =>
                $"CONVERT(nvarchar(4000), N'{EscapeString(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "")}')",
        };
    }

    private static string InferSqlVariantBaseType(object value) => value switch
    {
        bool => "bit",
        byte => "tinyint",
        short => "smallint",
        int => "int",
        long => "bigint",
        decimal => "decimal",
        double => "float",
        float => "real",
        DateTime => "datetime",
        DateTimeOffset => "datetimeoffset",
        Guid => "uniqueidentifier",
        TimeSpan => "time",
        byte[] => "varbinary",
        _ => "nvarchar",
    };

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string Numeric(object value)
        => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "NULL";

    private static string Decimal(object value)
        => Convert.ToDecimal(value).ToString(CultureInfo.InvariantCulture);

    private static DateTime ToDateTime(object value)
        => value is DateTime dt ? dt : Convert.ToDateTime(value, CultureInfo.InvariantCulture);

    /// <summary>Escapes single quotes for embedding in T-SQL string literals.</summary>
    public static string EscapeString(string s) => s.Replace("'", "''");
}
