using ArtSync.Abstractions;
using ArtSync.Data;
using FluentAssertions;
using Microsoft.Data.SqlClient;

namespace ArtSync.Integration.Tests;

/// <summary>
/// Verifies that every major SQL Server data type survives the full
/// hash → compare → script → apply → verify round-trip.
///
/// Uses dbo.TypeSampler which has one column per SQL type family.
/// Source starts with a fully-populated row; target starts empty.
/// After sync, target must contain an exact copy of each value.
/// </summary>
[Collection("Integration")]
public sealed class DataTypeIntegrationTests
{
    private static IOperationHandler Handler() =>
        new DataOperationHandler(new SqlDataCompare());

    private static CommandRequest SyncRequest() =>
        new(
            Operation:      OperationType.DataCompare,
            Source:         new Endpoint(EndpointKind.ConnectionString, ConnectionString: TestEnvironment.SrcCs),
            Target:         new Endpoint(EndpointKind.ConnectionString, ConnectionString: TestEnvironment.TgtCs),
            SyncMode:       SyncMode.Apply,
            SyncFilePath:   null,
            ArgFilePath:    null,
            CompFilePath:   null,
            FilterFilePath: null,
            ReportPath:     null,
            LogPath:        null,
            ReportFormat:   null,
            Quiet:          true,
            Argv0:          "datacompare",
            Options:        new Dictionary<string, string>(),
            Warnings:       Array.Empty<string>()
        );

    // ── Full-type sync ────────────────────────────────────────────────────────

    [IntegrationFact]
    public void AllTypes_SyncApply_Returns0_AndTargetRowMatchesSource()
    {
        EnsureTargetEmpty();

        var result = Handler().Run(SyncRequest());
        result.ExitCode.Should().Be(0, $"sync failed: {result.Message}");

        var src = ReadSamplerRow(TestEnvironment.SrcCs, 1);
        var tgt = ReadSamplerRow(TestEnvironment.TgtCs, 1);

        tgt.Should().NotBeNull(because: "the row should have been inserted");

        AssertEqual(src, tgt!, "ColBigInt");
        AssertEqual(src, tgt!, "ColSmallInt");
        AssertEqual(src, tgt!, "ColTinyInt");
        AssertEqual(src, tgt!, "ColBit");
        AssertEqual(src, tgt!, "ColDecimal");
        AssertEqual(src, tgt!, "ColNumeric");
        AssertEqual(src, tgt!, "ColMoney");
        AssertEqual(src, tgt!, "ColSmallMoney");
        AssertEqual(src, tgt!, "ColFloat");
        AssertEqual(src, tgt!, "ColReal");
        AssertEqual(src, tgt!, "ColDate");
        AssertEqual(src, tgt!, "ColTime");
        AssertEqual(src, tgt!, "ColDateTime");
        AssertEqual(src, tgt!, "ColDateTime2");
        AssertEqual(src, tgt!, "ColSmallDateTime");
        AssertEqual(src, tgt!, "ColDateTimeOffset");
        AssertEqual(src, tgt!, "ColChar");
        AssertEqual(src, tgt!, "ColNChar");
        AssertEqual(src, tgt!, "ColVarChar");
        AssertEqual(src, tgt!, "ColNVarChar");
        AssertEqualBytes(src, tgt!, "ColBinary");
        AssertEqualBytes(src, tgt!, "ColVarBinary");
        AssertEqual(src, tgt!, "ColGuid");
    }

    [IntegrationFact]
    public void NullValues_SyncApply_PreservesNullInTarget()
    {
        EnsureTargetEmpty();

        // Insert a row with all NULLs in source (only SamplerId populated by identity).
        EnsureSrcHasNullRow();

        Handler().Run(SyncRequest()).ExitCode.Should().BeOneOf([0, 112],
            "sync should succeed or report nothing-to-do");

        // Null row (SamplerId=2) must now exist in target and all data cols must be NULL.
        var tgtRow = ReadSamplerRow(TestEnvironment.TgtCs, 2);
        tgtRow.Should().NotBeNull(because: "null row should have been inserted");

        foreach (var key in tgtRow!.Keys.Where(k => k != "SamplerId"))
            tgtRow[key].Should().BeNull($"{key} should be NULL");
    }

    [IntegrationFact]
    public void UpdatedRow_SyncApply_OverwritesTargetValues()
    {
        EnsureTargetEmpty();

        // Seed target with a stale copy (different values).
        EnsureTargetHasStaleRow();

        var result = Handler().Run(SyncRequest());
        result.ExitCode.Should().Be(0, $"update sync failed: {result.Message}");

        // After sync, target value must match source.
        var src = ReadSamplerRow(TestEnvironment.SrcCs, 1);
        var tgt = ReadSamplerRow(TestEnvironment.TgtCs, 1);

        tgt.Should().NotBeNull();
        AssertEqual(src, tgt!, "ColBit");
        AssertEqual(src, tgt!, "ColDecimal");
        AssertEqual(src, tgt!, "ColNVarChar");
        AssertEqualBytes(src, tgt!, "ColVarBinary");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void EnsureTargetEmpty()
    {
        TestEnvironment.ExecTgt("""
            IF OBJECT_ID('dbo.TypeSampler') IS NOT NULL
            BEGIN
                ALTER TABLE dbo.TypeSampler NOCHECK CONSTRAINT ALL;
                DELETE FROM dbo.TypeSampler;
            END
            """);
    }

    private static void EnsureSrcHasNullRow()
    {
        // Insert only if row 2 doesn't already exist.
        TestEnvironment.ExecSrc("""
            IF NOT EXISTS (SELECT 1 FROM dbo.TypeSampler WHERE SamplerId = 2)
            BEGIN
                SET IDENTITY_INSERT dbo.TypeSampler ON;
                INSERT INTO dbo.TypeSampler (SamplerId) VALUES (2);
                SET IDENTITY_INSERT dbo.TypeSampler OFF;
            END
            """);
    }

    private static void EnsureTargetHasStaleRow()
    {
        TestEnvironment.ExecTgt("""
            IF NOT EXISTS (SELECT 1 FROM dbo.TypeSampler WHERE SamplerId = 1)
            BEGIN
                SET IDENTITY_INSERT dbo.TypeSampler ON;
                INSERT INTO dbo.TypeSampler (
                    SamplerId, ColBit, ColDecimal, ColNVarChar, ColVarBinary
                ) VALUES (
                    1, 0, 0.000000, N'stale value', 0x00000000
                );
                SET IDENTITY_INSERT dbo.TypeSampler OFF;
            END
            """);
    }

    private static Dictionary<string, object?>? ReadSamplerRow(string cs, int id)
    {
        using var conn = new SqlConnection(cs);
        conn.Open();
        using var cmd  = new SqlCommand(
            $"SELECT * FROM dbo.TypeSampler WHERE SamplerId = {id}", conn);
        using var rdr  = cmd.ExecuteReader();

        if (!rdr.Read()) return null;

        var row = new Dictionary<string, object?>();
        for (int i = 0; i < rdr.FieldCount; i++)
            row[rdr.GetName(i)] = rdr.IsDBNull(i) ? null : rdr.GetValue(i);
        return row;
    }

    private static void AssertEqual(
        Dictionary<string, object?>? src,
        Dictionary<string, object?> tgt,
        string col)
    {
        src.Should().NotBeNull();
        tgt.TryGetValue(col, out var tgtVal).Should().BeTrue($"column {col} should exist in target");
        src!.TryGetValue(col, out var srcVal).Should().BeTrue($"column {col} should exist in source");

        if (srcVal is null)
        {
            tgtVal.Should().BeNull($"{col}: expected NULL in target");
            return;
        }

        // For char/nchar SQL Server pads with spaces — trim for comparison.
        if (srcVal is string sv && tgtVal is string tv)
            tv.TrimEnd().Should().Be(sv.TrimEnd(), because: $"{col} string value should match");
        else
            tgtVal.Should().Be(srcVal, because: $"{col} value should match source");
    }

    private static void AssertEqualBytes(
        Dictionary<string, object?>? src,
        Dictionary<string, object?> tgt,
        string col)
    {
        src.Should().NotBeNull();
        src!.TryGetValue(col, out var srcVal);
        tgt.TryGetValue(col, out var tgtVal);

        if (srcVal is null) { tgtVal.Should().BeNull($"{col} expected NULL"); return; }

        tgtVal.Should().BeOfType<byte[]>($"{col} should be byte[]");
        ((byte[])tgtVal!).Should().Equal((byte[])srcVal, because: $"{col} bytes should match source");
    }
}
