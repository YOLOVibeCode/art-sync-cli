using ArtSync.Data;
using FluentAssertions;

namespace ArtSync.Data.Tests;

/// <summary>
/// Type-fixture matrix for <see cref="CanonicalPayload"/>.
/// Each test covers a SQL type → TSQL expression mapping and validates the
/// hash canonical-payload rules in SPEC §10.2.
/// </summary>
public sealed class CanonicalPayloadTests
{
    private static readonly IReadOnlyDictionary<string, string> NoOpts =
        new Dictionary<string, string>();

    // ── Integer types ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("int")]
    [InlineData("bigint")]
    [InlineData("smallint")]
    [InlineData("tinyint")]
    public void IntegerTypes_ProduceCast(string sqlType)
    {
        var expr = CanonicalPayload.BuildExpression("[Id]", sqlType, NoOpts);
        expr.Should().StartWith("CAST(");
        expr.Should().Contain("NVARCHAR(20)");
    }

    // ── String types ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("nvarchar")]
    [InlineData("varchar")]
    [InlineData("char")]
    [InlineData("nchar")]
    public void StringTypes_NoOptions_ReturnRawColumn(string sqlType)
    {
        var expr = CanonicalPayload.BuildExpression("[Name]", sqlType, NoOpts);
        // With no normalisation options the expression should just be the column.
        expr.Should().Contain("[Name]");
    }

    [Fact]
    public void IgnoreCase_WrapsInUpper()
    {
        var opts = new Dictionary<string, string> { ["IgnoreCase"] = "yes" };
        var expr = CanonicalPayload.BuildExpression("[Name]", "nvarchar", opts);
        expr.Should().StartWith("UPPER(");
    }

    [Fact]
    public void IgnoreLeadingSpaces_WrapsInLtrim()
    {
        var opts = new Dictionary<string, string> { ["IgnoreLeadingSpaces"] = "yes" };
        var expr = CanonicalPayload.BuildExpression("[Col]", "nvarchar", opts);
        expr.Should().Contain("LTRIM(");
    }

    [Fact]
    public void IgnoreTrailingSpaces_WrapsInRtrim()
    {
        var opts = new Dictionary<string, string> { ["IgnoreTrailingSpaces"] = "yes" };
        var expr = CanonicalPayload.BuildExpression("[Col]", "nvarchar", opts);
        expr.Should().Contain("RTRIM(");
    }

    [Fact]
    public void NonUnicodeVarchar_CastToNVarchar()
    {
        var expr = CanonicalPayload.BuildExpression("[Col]", "varchar", NoOpts);
        expr.Should().Contain("NVARCHAR(MAX)");
    }

    // ── DateTime types ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("datetime")]
    [InlineData("datetime2")]
    [InlineData("datetimeoffset")]
    public void DateTimeTypes_NoIgnoreTime_ProducesIso126Format(string sqlType)
    {
        var expr = CanonicalPayload.BuildExpression("[CreatedAt]", sqlType, NoOpts);
        expr.Should().Contain("126");
    }

    [Fact]
    public void DateType_ProducesDateFormat()
    {
        var expr = CanonicalPayload.BuildExpression("[BirthDate]", "date", NoOpts);
        expr.Should().Contain("126");
    }

    [Fact]
    public void TimeType_ProducesFmt114()
    {
        var expr = CanonicalPayload.BuildExpression("[StartTime]", "time", NoOpts);
        expr.Should().Contain("114");
    }

    [Theory]
    [InlineData("datetime")]
    [InlineData("datetime2")]
    public void DateTimeWithIgnoreTime_CastsToDate(string sqlType)
    {
        var opts = new Dictionary<string, string> { ["IsIgnoreTime"] = "yes" };
        var expr = CanonicalPayload.BuildExpression("[OrderDate]", sqlType, opts);
        // Should contain DATE cast and 126 for ISO
        expr.Should().Contain("DATE").And.Contain("126");
    }

    // ── Float types ───────────────────────────────────────────────────────────

    [Fact]
    public void FloatType_NoRound_CastToNVarchar()
    {
        var expr = CanonicalPayload.BuildExpression("[Price]", "float", NoOpts);
        expr.Should().Contain("CAST(");
        expr.Should().Contain("NVARCHAR(50)");
        expr.Should().NotContain("ROUND");
    }

    [Fact]
    public void FloatType_WithRound_UsesRound()
    {
        var opts = new Dictionary<string, string> { ["RoundFloatTypes"] = "yes" };
        var expr = CanonicalPayload.BuildExpression("[Price]", "float", opts);
        expr.Should().Contain("ROUND(");
    }

    // ── Binary types ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("binary")]
    [InlineData("varbinary")]
    [InlineData("rowversion")]
    [InlineData("timestamp")]
    public void BinaryTypes_UseHexConvert(string sqlType)
    {
        var expr = CanonicalPayload.BuildExpression("[Data]", sqlType, NoOpts);
        expr.Should().Contain("CONVERT(").And.Contain(", 2)");
    }

    // ── Bit type ──────────────────────────────────────────────────────────────

    [Fact]
    public void BitType_CastsToNVarchar5()
    {
        var expr = CanonicalPayload.BuildExpression("[IsActive]", "bit", NoOpts);
        expr.Should().Contain("NVARCHAR(5)");
    }

    // ── Uniqueidentifier ──────────────────────────────────────────────────────

    [Fact]
    public void Uniqueidentifier_UppercaseNormalised()
    {
        var expr = CanonicalPayload.BuildExpression("[RowGuid]", "uniqueidentifier", NoOpts);
        expr.Should().StartWith("UPPER(");
        expr.Should().Contain("NVARCHAR(40)");
    }

    // ── Decimal / money ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("decimal")]
    [InlineData("numeric")]
    [InlineData("money")]
    [InlineData("smallmoney")]
    public void DecimalTypes_CastToNVarchar50(string sqlType)
    {
        var expr = CanonicalPayload.BuildExpression("[Amount]", sqlType, NoOpts);
        expr.Should().Contain("NVARCHAR(50)");
    }

    // ── XML ───────────────────────────────────────────────────────────────────

    [Fact]
    public void XmlType_CastToNVarcharMax()
    {
        var expr = CanonicalPayload.BuildExpression("[Metadata]", "xml", NoOpts);
        expr.Should().Contain("NVARCHAR(MAX)");
    }

    // ── Unknown type ──────────────────────────────────────────────────────────

    [Fact]
    public void UnknownType_FallsBackToNVarcharMax()
    {
        var expr = CanonicalPayload.BuildExpression("[Col]", "geography", NoOpts);
        expr.Should().Contain("NVARCHAR(MAX)");
    }

    // ── NULL-safe wrapper ─────────────────────────────────────────────────────

    [Fact]
    public void NullSafe_NotNullable_ReturnsSameExpression()
    {
        const string expr = "CAST([Id] AS NVARCHAR(20))";
        CanonicalPayload.NullSafe(expr, isNullable: false, NoOpts).Should().Be(expr);
    }

    [Fact]
    public void NullSafe_Nullable_WrapsInCoalesce()
    {
        const string expr = "CAST([Id] AS NVARCHAR(20))";
        var safe = CanonicalPayload.NullSafe(expr, isNullable: true, NoOpts);
        safe.Should().StartWith("COALESCE(").And.Contain("NULL");
    }

    [Fact]
    public void NullSafe_EmptyStringEqualsNull_UseEmptyString()
    {
        var opts = new Dictionary<string, string> { ["IsEmptyStringEqualsNull"] = "yes" };
        const string expr = "[Name]";
        var safe = CanonicalPayload.NullSafe(expr, isNullable: true, opts);
        safe.Should().Contain("N''");
        safe.Should().NotContain("\x01NULL\x01");
    }

    [Fact]
    public void NullSafe_DefaultNullSentinel_DistinctFromEmpty()
    {
        const string expr = "[Name]";
        var safe = CanonicalPayload.NullSafe(expr, isNullable: true, NoOpts);
        // Sentinel must be present so NULL ≠ empty string
        safe.Should().Contain("\x01NULL\x01");
    }
}
