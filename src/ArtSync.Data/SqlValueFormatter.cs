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
///   <item><term>binary / varbinary</term><description>byte[]</description></item>
///   <item><term>uniqueidentifier</term><description>Guid</description></item>
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
            "binary" or "varbinary" =>
                value is byte[] ba
                    ? (ba.Length == 0 ? "0x" : $"0x{Convert.ToHexString(ba)}")
                    : "NULL",

            // ── GUID — quoted, braces stripped ───────────────────────────────
            "uniqueidentifier" =>
                value is Guid g
                    ? $"'{g:D}'"    // "D" = 32 hex digits with hyphens, no braces
                    : $"'{EscapeString(value.ToString()!)}'",

            // ── String / XML — N'...' with escaped single quotes ──────────────
            _ =>
                $"N'{EscapeString(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "")}'",
        };
    }

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
