using ArtSync.Abstractions;
using FluentAssertions;

namespace ArtSync.Schema.Tests;

/// <summary>
/// Phase 5 soak tests: run the same command line against both Devart and ArtSync
/// on restored copies of the same database pair and verify exit codes and
/// changed-object sets match (SPEC §16.4).
///
/// These tests are skipped until:
///   1. A SQL Server test fixture is provisioned (see tests/fixtures/README.md).
///   2. The two database snapshots are restored.
///   3. Environment variables are set (see below).
///
/// Required environment variables when running live:
///   ARTSYNC_SRC_CS   — source connection string  (on-prem / left side)
///   ARTSYNC_TGT_CS   — target connection string  (Azure SQL / right side)
///
/// Optional:
///   DEVART_PATH      — path to dbforgesql.com for parallel Devart execution
/// </summary>
public sealed class SoakTestStubs
{
    private const string SkipReason =
        "Requires live SQL Server pair. " +
        "Set ARTSYNC_SRC_CS and ARTSYNC_TGT_CS to enable.";

    // ── Schema soak tests ─────────────────────────────────────────────────────

    [Fact(Skip = SkipReason)]
    public void Schema_IdenticalDatabases_Returns100()
    {
        // Setup: restore the same .bacpac to both src and tgt.
        // Run: /schemacompare /source connection:… /target connection:…
        // Expect: exit 100 (identical).
        var (src, tgt) = GetConnectionStrings();
        var handler = BuildSchemaHandler();
        var req = MakeSchemaRequest(src, tgt, SyncMode.None);

        handler.Run(req).ExitCode.Should().Be(100);
    }

    [Fact(Skip = SkipReason)]
    public void Schema_ExtraTableOnSource_Returns101_ThenSyncReturns0()
    {
        // Setup: restore src with extra table, tgt without it.
        // Compare: expect exit 101 + diff list contains the table.
        // Apply:   expect exit 0; target now has the table.
        var (src, tgt) = GetConnectionStrings();
        var handler = BuildSchemaHandler();

        var compareReq = MakeSchemaRequest(src, tgt, SyncMode.None);
        var compareResult = handler.Run(compareReq);
        compareResult.ExitCode.Should().Be(101);

        var syncReq = MakeSchemaRequest(src, tgt, SyncMode.Apply);
        var syncResult = handler.Run(syncReq);
        syncResult.ExitCode.Should().Be(0);
    }

    [Fact(Skip = SkipReason)]
    public void Schema_WithFilter_ExcludedTablesNotInDiff()
    {
        // Setup: src has dbo.AuditLog extra. Filter excludes Table/dbo.Audit*.
        // Expect: exit 100 (no visible diff after filter).
        var (src, tgt) = GetConnectionStrings();
        var filterPath = Path.Combine(
            TestFixturesDir(), "schema-exclude-audit.scflt");
        if (!File.Exists(filterPath))
            throw new SkipException($"Filter fixture not found: {filterPath}");

        var handler = BuildSchemaHandler();
        var req = MakeSchemaRequest(src, tgt, SyncMode.None, filterPath);
        handler.Run(req).ExitCode.Should().Be(100);
    }

    [Fact(Skip = SkipReason)]
    public void Schema_BrokenConnection_Returns40()
    {
        var handler = BuildSchemaHandler();
        var req = MakeSchemaRequest(
            src: "Server=does-not-exist.invalid;Database=X;Integrated Security=True",
            tgt: "Server=also-invalid;Database=Y;Integrated Security=True",
            syncMode: SyncMode.None);
        handler.Run(req).ExitCode.Should().Be(40);
    }

    [Fact(Skip = SkipReason)]
    public void Schema_SyncScript_DoesNotModifyTarget()
    {
        // Run /sync:file and verify the target is unchanged afterwards.
        var (src, tgt) = GetConnectionStrings();
        var scriptPath = Path.GetTempFileName();
        try
        {
            var handler = BuildSchemaHandler();
            var req = MakeSchemaRequest(src, tgt, SyncMode.ScriptFile, syncFilePath: scriptPath);
            var result = handler.Run(req);

            result.ExitCode.Should().BeOneOf(100, 101);
            if (result.ExitCode == 101)
            {
                File.Exists(scriptPath).Should().BeTrue();
                File.ReadAllText(scriptPath).Should().NotBeNullOrWhiteSpace();
            }
            // A subsequent compare should still report the same state (no apply happened)
            var verifyReq = MakeSchemaRequest(src, tgt, SyncMode.None);
            handler.Run(verifyReq).ExitCode.Should().Be(result.ExitCode == 100 ? 100 : 101);
        }
        finally { File.Delete(scriptPath); }
    }

    // ── Data soak tests ───────────────────────────────────────────────────────

    [Fact(Skip = SkipReason)]
    public void Data_IdenticalTables_Returns100()
    {
        // Not yet implemented in test scaffold.
        // Implement using DataOperationHandler + live IDataCompare when ready.
        throw new SkipException("DataOperationHandler live soak not yet scaffolded.");
    }

    [Fact(Skip = SkipReason)]
    public void Data_RowOnlyInSource_SyncApply_Returns0_And_RowAppearsInTarget()
    {
        throw new SkipException("DataOperationHandler live soak not yet scaffolded.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (string src, string tgt) GetConnectionStrings()
    {
        var src = Environment.GetEnvironmentVariable("ARTSYNC_SRC_CS")
               ?? throw new InvalidOperationException("ARTSYNC_SRC_CS not set");
        var tgt = Environment.GetEnvironmentVariable("ARTSYNC_TGT_CS")
               ?? throw new InvalidOperationException("ARTSYNC_TGT_CS not set");
        return (src, tgt);
    }

    private static IOperationHandler BuildSchemaHandler()
        => new ArtSync.Schema.SchemaOperationHandler(
               new ArtSync.Schema.DacFxSchemaCompare());

    private static CommandRequest MakeSchemaRequest(
        string src,
        string tgt,
        SyncMode syncMode,
        string? filterPath = null,
        string? syncFilePath = null)
    {
        var srcEp = new Endpoint(EndpointKind.ConnectionString, ConnectionString: src);
        var tgtEp = new Endpoint(EndpointKind.ConnectionString, ConnectionString: tgt);

        return new CommandRequest(
            Operation: OperationType.SchemaCompare,
            Source: srcEp,
            Target: tgtEp,
            SyncMode: syncMode,
            SyncFilePath: syncFilePath,
            ArgFilePath: null,
            CompFilePath: null,
            FilterFilePath: filterPath,
            ReportPath: null,
            LogPath: null,
            ReportFormat: null,
            Quiet: true,
            Argv0: "schemacompare",
            Options: new Dictionary<string, string>
            {
                ["IgnorePermissions"] = "yes",
                ["IgnoreUserPermissions"] = "yes",
            },
            Warnings: Array.Empty<string>()
        );
    }

    private static string TestFixturesDir()
    {
        var dir = Path.GetDirectoryName(typeof(SoakTestStubs).Assembly.Location)!;
        // Walk up to repo root, then down to tests/fixtures
        var repoRoot = dir;
        while (repoRoot is not null && !File.Exists(Path.Combine(repoRoot, "ArtSync.slnx")))
            repoRoot = Path.GetDirectoryName(repoRoot);
        return Path.Combine(repoRoot ?? dir, "tests", "fixtures");
    }
}

/// <summary>Thrown inside skip-logic to produce a clean skip message in CI.</summary>
internal sealed class SkipException : Exception
{
    public SkipException(string reason) : base(reason) { }
}
