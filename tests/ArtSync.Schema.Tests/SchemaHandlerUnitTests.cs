using ArtSync.Abstractions;
using ArtSync.Compat;
using ArtSync.Schema;
using FluentAssertions;

namespace ArtSync.Schema.Tests;

/// <summary>
/// Unit tests for SchemaOperationHandler using a fake ISchemaCompare.
/// No SQL Server required.
///
/// Exit code contract (SPEC §8):
///   100 — identical (compare-only)
///   101 — differences (compare-only OR /sync:file)
///   108 — no comparable objects
///   112 — nothing to sync (/sync on identical)
///   0   — sync applied
///   10  — bad request
///   40  — connection failure
/// </summary>
public sealed class SchemaHandlerUnitTests
{
    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static SchemaOperationHandler Handler(FakeSchemaCompare fake)
        => new(fake);

    private static CommandRequest MakeRequest(
        SyncMode syncMode = SyncMode.None,
        string? syncFilePath = null,
        string? compFile = null,
        bool missingSource = false,
        bool missingTarget = false)
    {
        var src = missingSource ? null : new Endpoint(EndpointKind.LiveSplit, "Src", "SrcDb");
        var tgt = missingTarget ? null : new Endpoint(EndpointKind.LiveSplit, "Tgt", "TgtDb");

        return new CommandRequest(
            Operation: OperationType.SchemaCompare,
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
            Argv0: "dbforgesql",
            Options: new Dictionary<string, string>(),
            Warnings: Array.Empty<string>()
        );
    }

    // ─── Plan TDD case: Two identical databases → exit 100 ───────────────────

    [Fact]
    public void Identical_CompareOnly_Returns100()
    {
        var fake = new FakeSchemaCompare(
            info: new SchemaCompareInfo(true, false, 0, [], []));

        var result = Handler(fake).Run(MakeRequest(SyncMode.None));

        result.ExitCode.Should().Be(100);
        fake.CompareCalled.Should().BeTrue();
    }

    [Fact]
    public void Identical_SyncApply_Returns112_NothingToSync()
    {
        var fake = new FakeSchemaCompare(
            info: new SchemaCompareInfo(true, false, 0, [], []));

        var result = Handler(fake).Run(MakeRequest(SyncMode.Apply));

        result.ExitCode.Should().Be(112);
    }

    // ─── Plan TDD case: Extra table on source → differences → script/apply ──

    [Fact]
    public void HasDifferences_CompareOnly_Returns101()
    {
        var fake = new FakeSchemaCompare(
            info: new SchemaCompareInfo(false, false, 1, ["dbo.NewTable"], []),
            scriptContent: "CREATE TABLE [dbo].[NewTable]([Id] INT);");

        var result = Handler(fake).Run(MakeRequest(SyncMode.None));

        result.ExitCode.Should().Be(101);
        fake.ScriptCalled.Should().BeFalse("script should not be generated on compare-only");
        fake.PublishCalled.Should().BeFalse();
    }

    [Fact]
    public void HasDifferences_SyncScriptFile_Returns101_AndWritesFile()
    {
        var tmpFile = Path.GetTempFileName();
        try
        {
            const string expectedSql = "CREATE TABLE [dbo].[NewTable]([Id] INT);";
            var fake = new FakeSchemaCompare(
                info: new SchemaCompareInfo(false, false, 1, ["dbo.NewTable"], []),
                scriptContent: expectedSql);

            var req = MakeRequest(SyncMode.ScriptFile, syncFilePath: tmpFile);
            var result = Handler(fake).Run(req);

            result.ExitCode.Should().Be(101);
            File.ReadAllText(tmpFile).Should().Be(expectedSql);
            fake.ScriptCalled.Should().BeTrue();
            fake.PublishCalled.Should().BeFalse();
        }
        finally { File.Delete(tmpFile); }
    }

    [Fact]
    public void HasDifferences_SyncApply_Returns0()
    {
        var fake = new FakeSchemaCompare(
            info: new SchemaCompareInfo(false, false, 1, ["dbo.NewTable"], []));

        var result = Handler(fake).Run(MakeRequest(SyncMode.Apply));

        result.ExitCode.Should().Be(0);
        fake.PublishCalled.Should().BeTrue();
        fake.ScriptCalled.Should().BeFalse();
    }

    // ─── No objects after filters → exit 108 ─────────────────────────────────

    [Fact]
    public void NoComparableObjects_Returns108()
    {
        var fake = new FakeSchemaCompare(
            info: new SchemaCompareInfo(false, true, 0, [], []));

        var result = Handler(fake).Run(MakeRequest(SyncMode.None));

        result.ExitCode.Should().Be(108);
    }

    // ─── /sync:file does not modify target ───────────────────────────────────

    [Fact]
    public void SyncScriptFile_DoesNotCallPublish()
    {
        var fake = new FakeSchemaCompare(
            info: new SchemaCompareInfo(false, false, 3, ["a", "b", "c"], []),
            scriptContent: "-- diff");

        var tmpFile = Path.GetTempFileName();
        try
        {
            Handler(fake).Run(MakeRequest(SyncMode.ScriptFile, syncFilePath: tmpFile));
            fake.PublishCalled.Should().BeFalse();
        }
        finally { File.Delete(tmpFile); }
    }

    // ─── Broken connection → exit 40 ─────────────────────────────────────────

    [Fact]
    public void ConnectionFailure_Returns40()
    {
        var fake = new FakeSchemaCompare(
            throwOnCompare: new SchemaConnectionException("Cannot open server 'SrcServer'."));

        var result = Handler(fake).Run(MakeRequest(SyncMode.None));

        result.ExitCode.Should().Be(40);
    }

    // ─── Validation: missing source / target ──────────────────────────────────

    [Fact]
    public void MissingSource_Returns10()
    {
        var fake = new FakeSchemaCompare(
            info: new SchemaCompareInfo(true, false, 0, [], []));

        Handler(fake).Run(MakeRequest(missingSource: true))
            .ExitCode.Should().Be(10);
    }

    [Fact]
    public void MissingTarget_Returns10()
    {
        var fake = new FakeSchemaCompare(
            info: new SchemaCompareInfo(true, false, 0, [], []));

        Handler(fake).Run(MakeRequest(missingTarget: true))
            .ExitCode.Should().Be(10);
    }

    [Fact]
    public void CompFile_Returns10_UntilImplemented()
    {
        var fake = new FakeSchemaCompare(
            info: new SchemaCompareInfo(true, false, 0, [], []));

        Handler(fake).Run(MakeRequest(compFile: "schema.scomp"))
            .ExitCode.Should().Be(10);
    }

    // ─── Filter file: missing .scflt → exit 114 ───────────────────────────────

    [Fact]
    public void MissingFilterFile_Returns114()
    {
        var fake = new FakeSchemaCompare(
            throwOnOpenSession: new SchemaFilterException("Filter file not found: 'missing.scflt'"));

        var req = MakeRequestWithFilter("missing.scflt");
        Handler(fake).Run(req).ExitCode.Should().Be(114);
    }

    [Fact]
    public void FilterFilePath_IsPassedInOptions()
    {
        var fake = new FakeSchemaCompare(
            info: new SchemaCompareInfo(true, false, 0, [], []));

        var req = MakeRequestWithFilter(@"C:\filters\schema.scflt");
        Handler(fake).Run(req);

        fake.OptionsReceived.Should().ContainKey("_FilterFilePath");
        fake.OptionsReceived!["_FilterFilePath"].Should().Be(@"C:\filters\schema.scflt");
    }

    // ─── Helpers (filter-specific) ────────────────────────────────────────────

    private static CommandRequest MakeRequestWithFilter(string filterPath) =>
        new CommandRequest(
            Operation: OperationType.SchemaCompare,
            Source: new Endpoint(EndpointKind.LiveSplit, "Src", "SrcDb"),
            Target: new Endpoint(EndpointKind.LiveSplit, "Tgt", "TgtDb"),
            SyncMode: SyncMode.None,
            SyncFilePath: null,
            ArgFilePath: null,
            CompFilePath: null,
            FilterFilePath: filterPath,
            ReportPath: null,
            LogPath: null,
            ReportFormat: null,
            Quiet: false,
            Argv0: "schemacompare",
            Options: new Dictionary<string, string>(),
            Warnings: Array.Empty<string>()
        );
}

// ─── Test doubles ─────────────────────────────────────────────────────────────

internal sealed class FakeSchemaCompare : ISchemaCompare
{
    private readonly SchemaCompareInfo? _info;
    private readonly string? _scriptContent;
    private readonly Exception? _throwOnCompare;
    private readonly Exception? _throwOnOpenSession;

    public bool CompareCalled { get; private set; }
    public bool ScriptCalled { get; private set; }
    public bool PublishCalled { get; private set; }
    public IReadOnlyDictionary<string, string>? OptionsReceived { get; private set; }

    public FakeSchemaCompare(
        SchemaCompareInfo? info = null,
        string? scriptContent = null,
        Exception? throwOnCompare = null,
        Exception? throwOnOpenSession = null)
    {
        _info = info;
        _scriptContent = scriptContent;
        _throwOnCompare = throwOnCompare;
        _throwOnOpenSession = throwOnOpenSession;
    }

    public ISchemaSession OpenSession(
        Endpoint source,
        Endpoint target,
        IReadOnlyDictionary<string, string> options)
    {
        OptionsReceived = options;
        if (_throwOnOpenSession is not null)
            throw _throwOnOpenSession;
        return new FakeSession(this);
    }

    private sealed class FakeSession(FakeSchemaCompare parent) : ISchemaSession
    {
        public SchemaCompareInfo Compare()
        {
            parent.CompareCalled = true;
            if (parent._throwOnCompare is not null)
                throw parent._throwOnCompare;
            return parent._info!;
        }

        public string? GenerateScript()
        {
            parent.ScriptCalled = true;
            return parent._scriptContent;
        }

        public void Publish() => parent.PublishCalled = true;

        public void Dispose() { }
    }
}
