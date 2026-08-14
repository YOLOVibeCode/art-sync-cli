using ArtSync.Abstractions;
using ArtSync.Data;
using FluentAssertions;

namespace ArtSync.Data.Tests;

/// <summary>
/// Unit tests for DataOperationHandler via fake IDataCompare.
/// Exit code contract (SPEC §8):
///   100 — identical rows
///   101 — differences (compare-only OR /sync:file)
///   108 — no comparable tables
///   112 — nothing to sync (/sync on identical)
///   0   — sync applied
///   10  — bad request
///   40  — connection failure
/// </summary>
public sealed class DataHandlerUnitTests
{
    // ─── Plan TDD case 1: Identical tables → exit 100 ────────────────────────

    [Fact]
    public void Identical_CompareOnly_Returns100()
    {
        var fake = new FakeDataCompare(new DataCompareInfo(
            DataCompareStatus.Identical, 0, 0, 0, 0, ["dbo.Orders"], []));

        DataHandler(fake).Run(MakeRequest(SyncMode.None)).ExitCode.Should().Be(100);
    }

    [Fact]
    public void Identical_SyncApply_Returns112()
    {
        var fake = new FakeDataCompare(new DataCompareInfo(
            DataCompareStatus.Identical, 0, 0, 0, 0, ["dbo.Orders"], []));

        DataHandler(fake).Run(MakeRequest(SyncMode.Apply)).ExitCode.Should().Be(112);
    }

    // ─── Plan TDD case 2: Row only in source → INSERT on apply ───────────────

    [Fact]
    public void SourceOnlyRow_CompareOnly_Returns101()
    {
        var diffs = new[] { new RowDiff("dbo.Orders", RowDiffKind.OnlyInSource, [("Id", (object?)1)]) };
        var fake = new FakeDataCompare(
            new DataCompareInfo(DataCompareStatus.HasDifferences, 1, 1, 0, 0, ["dbo.Orders"], []),
            diffs: diffs, script: "INSERT INTO [dbo].[Orders]([Id]) VALUES(1);");

        DataHandler(fake).Run(MakeRequest(SyncMode.None)).ExitCode.Should().Be(101);
        fake.ScriptCalled.Should().BeFalse();
    }

    [Fact]
    public void SourceOnlyRow_SyncScriptFile_Returns101_WritesFile()
    {
        var diffs = new[] { new RowDiff("dbo.Orders", RowDiffKind.OnlyInSource, [("Id", (object?)1)]) };
        const string expected = "INSERT INTO [dbo].[Orders]([Id]) VALUES(1);";
        var fake = new FakeDataCompare(
            new DataCompareInfo(DataCompareStatus.HasDifferences, 1, 1, 0, 0, ["dbo.Orders"], []),
            diffs: diffs, script: expected);

        var tmp = Path.GetTempFileName();
        try
        {
            var result = DataHandler(fake).Run(MakeRequest(SyncMode.ScriptFile, tmp));
            result.ExitCode.Should().Be(101);
            File.ReadAllText(tmp).Should().Be(expected);
            fake.ScriptCalled.Should().BeTrue();
            fake.ApplyCalled.Should().BeFalse();
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void SourceOnlyRow_SyncApply_Returns0()
    {
        var diffs = new[] { new RowDiff("dbo.Orders", RowDiffKind.OnlyInSource, [("Id", (object?)1)]) };
        var fake = new FakeDataCompare(
            new DataCompareInfo(DataCompareStatus.HasDifferences, 1, 1, 0, 0, ["dbo.Orders"], []),
            diffs: diffs, script: "INSERT ...");

        DataHandler(fake).Run(MakeRequest(SyncMode.Apply)).ExitCode.Should().Be(0);
        fake.ApplyCalled.Should().BeTrue();
    }

    // ─── Plan TDD case 3: Row only in target → DELETE on apply ───────────────

    [Fact]
    public void TargetOnlyRow_CompareOnly_Returns101()
    {
        var diffs = new[] { new RowDiff("dbo.Orders", RowDiffKind.OnlyInTarget, [("Id", (object?)99)]) };
        var fake = new FakeDataCompare(
            new DataCompareInfo(DataCompareStatus.HasDifferences, 1, 0, 1, 0, ["dbo.Orders"], []),
            diffs: diffs, script: "DELETE FROM [dbo].[Orders] WHERE [Id]=99;");

        DataHandler(fake).Run(MakeRequest(SyncMode.None)).ExitCode.Should().Be(101);
    }

    // ─── Plan TDD case 4: Same PK, different column → UPDATE on apply ────────

    [Fact]
    public void DifferentColumn_SyncApply_Returns0_AndCallsApply()
    {
        var diffs = new[] { new RowDiff("dbo.Orders", RowDiffKind.Different, [("Id", (object?)7)]) };
        var fake = new FakeDataCompare(
            new DataCompareInfo(DataCompareStatus.HasDifferences, 1, 0, 0, 1, ["dbo.Products"], []),
            diffs: diffs, script: "UPDATE ...");

        DataHandler(fake).Run(MakeRequest(SyncMode.Apply)).ExitCode.Should().Be(0);
        fake.ApplyCalled.Should().BeTrue();
    }

    // ─── Plan TDD case 5: Identity/rowversion ignored ─────────────────────────
    // Verified at this level by asserting the option is passed through to FakeDataCompare.

    [Fact]
    public void IgnoreIdentityColumns_OptionPassedThrough()
    {
        var fake = new FakeDataCompare(
            new DataCompareInfo(DataCompareStatus.Identical, 0, 0, 0, 0, ["dbo.T"], []));

        var req = MakeRequest(SyncMode.None, options: new Dictionary<string, string>
        {
            ["IgnoreIdentityColumns"] = "yes",
        });

        DataHandler(fake).Run(req);
        fake.OptionsReceived.Should().ContainKey("IgnoreIdentityColumns");
    }

    // ─── Plan TDD case 6: Masked-out table is not compared ────────────────────

    [Fact]
    public void ExcludedTable_NoObjectsToCompare_Returns108()
    {
        var fake = new FakeDataCompare(new DataCompareInfo(
            DataCompareStatus.NoComparableTables, 0, 0, 0, 0, [], []));

        DataHandler(fake).Run(MakeRequest(SyncMode.None)).ExitCode.Should().Be(108);
    }

    // ─── Validation ──────────────────────────────────────────────────────────

    [Fact]
    public void MissingSource_Returns10()
    {
        var fake = new FakeDataCompare(
            new DataCompareInfo(DataCompareStatus.Identical, 0, 0, 0, 0, [], []));

        DataHandler(fake).Run(MakeRequest(missingSource: true)).ExitCode.Should().Be(10);
    }

    [Fact]
    public void MissingTarget_Returns10()
    {
        var fake = new FakeDataCompare(
            new DataCompareInfo(DataCompareStatus.Identical, 0, 0, 0, 0, [], []));

        DataHandler(fake).Run(MakeRequest(missingTarget: true)).ExitCode.Should().Be(10);
    }

    [Fact]
    public void CompFile_Returns10_UntilImplemented()
    {
        var fake = new FakeDataCompare(
            new DataCompareInfo(DataCompareStatus.Identical, 0, 0, 0, 0, [], []));

        DataHandler(fake).Run(MakeRequest(compFile: "data.dcomp")).ExitCode.Should().Be(10);
    }

    // ─── Connection failure → exit 40 ─────────────────────────────────────────

    [Fact]
    public void ConnectionFailure_Returns40()
    {
        var fake = new FakeDataCompare(
            throwOnCompare: new DataConnectionException("Login failed."));

        DataHandler(fake).Run(MakeRequest(SyncMode.None)).ExitCode.Should().Be(40);
    }

    // ─── /sync:file does not apply ────────────────────────────────────────────

    [Fact]
    public void SyncScriptFile_DoesNotApply()
    {
        var diffs = new[] { new RowDiff("dbo.T", RowDiffKind.Different, [("Id", (object?)1)]) };
        var fake = new FakeDataCompare(
            new DataCompareInfo(DataCompareStatus.HasDifferences, 1, 0, 0, 1, ["dbo.T"], []),
            diffs: diffs, script: "UPDATE ...");

        var tmp = Path.GetTempFileName();
        try
        {
            DataHandler(fake).Run(MakeRequest(SyncMode.ScriptFile, tmp));
            fake.ApplyCalled.Should().BeFalse();
        }
        finally { File.Delete(tmp); }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static DataOperationHandler DataHandler(FakeDataCompare fake) => new(fake);

    private static CommandRequest MakeRequest(
        SyncMode syncMode = SyncMode.None,
        string? syncFilePath = null,
        string? compFile = null,
        bool missingSource = false,
        bool missingTarget = false,
        IReadOnlyDictionary<string, string>? options = null)
    {
        var src = missingSource ? null : new Endpoint(EndpointKind.LiveSplit, "Src", "SrcDb");
        var tgt = missingTarget ? null : new Endpoint(EndpointKind.LiveSplit, "Tgt", "TgtDb");

        return new CommandRequest(
            Operation: OperationType.DataCompare,
            Source: src,
            Target: tgt,
            SyncMode: syncMode,
            SyncFilePath: syncFilePath,
            ArgFilePath: null,
            CompFilePath: compFile,
            FilterFilePath: null,
            ReportPath: null,
            LogPath: null,
            ReportFormat: null,
            Quiet: false,
            Argv0: "datacompare",
            Options: options ?? new Dictionary<string, string>(),
            Warnings: Array.Empty<string>()
        );
    }
}

// ─── Test doubles ─────────────────────────────────────────────────────────────

internal sealed class FakeDataCompare : IDataCompare
{
    private readonly DataCompareInfo? _info;
    private readonly IReadOnlyList<RowDiff>? _diffs;
    private readonly string? _script;
    private readonly Exception? _throwOnCompare;

    public bool ScriptCalled { get; private set; }
    public bool ApplyCalled { get; private set; }
    public IReadOnlyDictionary<string, string>? OptionsReceived { get; private set; }

    public FakeDataCompare(
        DataCompareInfo? info = null,
        IReadOnlyList<RowDiff>? diffs = null,
        string? script = null,
        Exception? throwOnCompare = null)
    {
        _info = info;
        _diffs = diffs;
        _script = script;
        _throwOnCompare = throwOnCompare;
    }

    public DataCompareInfo Compare(
        Endpoint source,
        Endpoint target,
        IReadOnlyDictionary<string, string> options)
    {
        OptionsReceived = options;
        if (_throwOnCompare is not null) throw _throwOnCompare;
        return _info!;
    }

    public string Script(
        Endpoint source,
        Endpoint target,
        IReadOnlyList<RowDiff> diffs,
        IReadOnlyDictionary<string, string> options)
    {
        ScriptCalled = true;
        return _script ?? "-- no data";
    }

    public void Apply(
        Endpoint target,
        string script,
        IReadOnlyDictionary<string, string> options)
    {
        ApplyCalled = true;
    }
}
