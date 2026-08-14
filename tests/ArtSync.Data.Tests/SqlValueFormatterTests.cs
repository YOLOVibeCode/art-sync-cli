using ArtSync.Data;
using FluentAssertions;

namespace ArtSync.Data.Tests;

/// <summary>
/// Unit tests for <see cref="SqlValueFormatter.Format"/>.
/// Verifies that every SQL Server type produces a valid, correctly-typed T-SQL literal.
///
/// Key regression tests (previously all wrong):
///   bit    — bool.ToString() produced "True"/"False" instead of 1/0
///   float  — Convert.ToString() used locale decimal separator (comma in EU)
///   decimal— same
///   binary — Convert.ToString(byte[]) produced "System.Byte[]"
///   datetime — Convert.ToString(DateTime) used locale short-date format
/// </summary>
public sealed class SqlValueFormatterTests
{
    // ── NULL / DBNull ─────────────────────────────────────────────────────────

    [Fact]
    public void Null_ReturnsNULL()
        => SqlValueFormatter.Format(null, "int").Should().Be("NULL");

    [Fact]
    public void DBNull_ReturnsNULL()
        => SqlValueFormatter.Format(DBNull.Value, "nvarchar").Should().Be("NULL");

    // ── Integer types ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(42,    "int",      "42")]
    [InlineData(-1L,   "bigint",   "-1")]
    [InlineData((short)32767, "smallint", "32767")]
    [InlineData((byte)255, "tinyint", "255")]
    public void IntegerTypes_ProduceUnquotedLiteral(object value, string type, string expected)
        => SqlValueFormatter.Format(value, type).Should().Be(expected);

    // ── Bit ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Bit_True_Returns1()
        => SqlValueFormatter.Format(true, "bit").Should().Be("1");

    [Fact]
    public void Bit_False_Returns0()
        => SqlValueFormatter.Format(false, "bit").Should().Be("0");

    [Fact]
    public void Bit_BoolToString_WasbuggyShouldNot_ReturnTrue()
        => SqlValueFormatter.Format(true, "bit").Should().NotBe("True");

    // ── Decimal / money — invariant culture ──────────────────────────────────

    [Fact]
    public void Decimal_UsesInvariantDecimalSeparator()
        => SqlValueFormatter.Format(1234.56m, "decimal").Should().Be("1234.56");

    [Fact]
    public void Money_UsesInvariantDecimalSeparator()
        => SqlValueFormatter.Format(9.99m, "money").Should().Be("9.99");

    [Fact]
    public void Numeric_LargeValue_NoThousandsSeparator()
        => SqlValueFormatter.Format(1_234_567.89m, "numeric").Should().Be("1234567.89");

    [Fact]
    public void SmallMoney_NegativeValue()
        => SqlValueFormatter.Format(-0.01m, "smallmoney").Should().Be("-0.01");

    // ── Float / real — invariant, round-trip ─────────────────────────────────

    [Fact]
    public void Float_UsesRoundTripFormat()
    {
        var result = SqlValueFormatter.Format(3.141592653589793d, "float");
        result.Should().NotContain(",");   // no locale separator
        result.Should().Contain("3.14");
    }

    [Fact]
    public void Real_PositiveValue()
    {
        var result = SqlValueFormatter.Format(2.5f, "real");
        result.Should().Be("2.5");
    }

    [Fact]
    public void Float_NegativeValue_NoQuotes()
        => SqlValueFormatter.Format(-1.5d, "float").Should().NotStartWith("'");

    // ── DateTime types — ISO 8601, quoted ────────────────────────────────────

    [Fact]
    public void DateTime_ProducesIso8601()
    {
        var dt = new DateTime(2026, 1, 15, 14, 30, 0, 123, DateTimeKind.Unspecified);
        var result = SqlValueFormatter.Format(dt, "datetime");
        result.Should().StartWith("'2026-01-15 14:30:00");
        result.Should().EndWith("'");
        result.Should().NotContain("/");   // not locale format
    }

    [Fact]
    public void DateTime2_SubSecondPrecision()
    {
        var dt = new DateTime(2026, 8, 13, 19, 25, 33, 456, DateTimeKind.Utc)
                     .AddTicks(7890);
        var result = SqlValueFormatter.Format(dt, "datetime2");
        result.Should().Contain("2026-08-13");
        result.Should().Contain("19:25:33");
        result.Should().MatchRegex(@"'\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{7}'");
    }

    [Fact]
    public void Date_ProducesDateOnlyFormat()
    {
        var dt = new DateTime(2026, 3, 25);
        var result = SqlValueFormatter.Format(dt, "date");
        result.Should().Be("'2026-03-25'");
    }

    [Fact]
    public void Date_DoesNotIncludeTimePart()
    {
        var dt = new DateTime(2026, 3, 25, 10, 30, 0);
        SqlValueFormatter.Format(dt, "date").Should().Be("'2026-03-25'");
    }

    [Fact]
    public void Time_TimeSpanValue_QuotedHhMmSs()
    {
        var ts = new TimeSpan(0, 10, 30, 45, 123);
        var result = SqlValueFormatter.Format(ts, "time");
        result.Should().StartWith("'10:30:45");
        result.Should().EndWith("'");
    }

    [Fact]
    public void DateTimeOffset_IncludesOffset()
    {
        var dto = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.FromHours(5.5));
        var result = SqlValueFormatter.Format(dto, "datetimeoffset");
        result.Should().Contain("+05:30");
        result.Should().Contain("2026-06-01");
    }

    [Fact]
    public void SmallDateTime_NoFractionalSeconds()
    {
        var dt = new DateTime(2026, 12, 31, 23, 59, 0);
        var result = SqlValueFormatter.Format(dt, "smalldatetime");
        result.Should().Be("'2026-12-31 23:59:00'");
        result.Should().NotContain(".",
            because: "smalldatetime rejects fractional-second literals");
    }

    [Fact]
    public void DateTime_ThreeDecimalPlacesOnly()
    {
        var dt = new DateTime(2026, 1, 15, 14, 30, 0, 123);
        var result = SqlValueFormatter.Format(dt, "datetime");
        result.Should().MatchRegex(@"'\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}'",
            because: "datetime supports exactly 3 fractional digits");
    }

    // ── Binary types — 0xHEX ─────────────────────────────────────────────────

    [Fact]
    public void VarBinary_ProducesHexLiteral()
    {
        var bytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var result = SqlValueFormatter.Format(bytes, "varbinary");
        result.Should().Be("0xDEADBEEF");
    }

    [Fact]
    public void Binary_EmptyBytes_Returns0x()
    {
        var result = SqlValueFormatter.Format(Array.Empty<byte>(), "binary");
        result.Should().Be("0x");
    }

    [Fact]
    public void VarBinary_WasBuggy_DoesNotReturnSystemByteArray()
    {
        var bytes = new byte[] { 1, 2, 3 };
        SqlValueFormatter.Format(bytes, "varbinary").Should().NotContain("System.Byte");
    }

    [Fact]
    public void VarBinary_AllZeroBytes()
    {
        var bytes = new byte[] { 0x00, 0x00 };
        SqlValueFormatter.Format(bytes, "varbinary").Should().Be("0x0000");
    }

    // ── Uniqueidentifier ──────────────────────────────────────────────────────

    [Fact]
    public void Guid_ProducesQuotedFormatWithoutBraces()
    {
        var g = new Guid("12345678-1234-1234-1234-123456789abc");
        var result = SqlValueFormatter.Format(g, "uniqueidentifier");
        result.Should().Be("'12345678-1234-1234-1234-123456789abc'");
        result.Should().NotContain("{");
    }

    // ── String types ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("nvarchar")]
    [InlineData("varchar")]
    [InlineData("nchar")]
    [InlineData("char")]
    [InlineData("ntext")]
    [InlineData("text")]
    public void StringTypes_ProduceNPrefixedQuotedLiteral(string typeName)
    {
        var result = SqlValueFormatter.Format("Hello World", typeName);
        result.Should().Be("N'Hello World'");
    }

    [Fact]
    public void String_SingleQuoteEscaped()
        => SqlValueFormatter.Format("O'Brien", "nvarchar").Should().Be("N'O''Brien'");

    [Fact]
    public void String_EmptyString()
        => SqlValueFormatter.Format("", "nvarchar").Should().Be("N''");

    // ── XML ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Xml_ProducesNPrefixedString()
    {
        var xml = "<root><item id=\"1\"/></root>";
        var result = SqlValueFormatter.Format(xml, "xml");
        result.Should().StartWith("N'");
        result.Should().Contain("root");
    }

    // ── EscapeString helper ───────────────────────────────────────────────────

    [Fact]
    public void EscapeString_DoublesSingleQuotes()
        => SqlValueFormatter.EscapeString("it's fine").Should().Be("it''s fine");

    [Fact]
    public void EscapeString_MultipleQuotes()
        => SqlValueFormatter.EscapeString("a'b'c").Should().Be("a''b''c");

    [Fact]
    public void EscapeString_NoQuotes_Unchanged()
        => SqlValueFormatter.EscapeString("hello").Should().Be("hello");
}
