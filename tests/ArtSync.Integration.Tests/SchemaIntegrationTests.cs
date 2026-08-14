using ArtSync.Abstractions;
using ArtSync.Schema;
using FluentAssertions;

namespace ArtSync.Integration.Tests;

/// <summary>
/// End-to-end schema compare / sync tests using live SQL Server databases.
///
/// Precondition: run ./scripts/run-integration-tests.sh (starts Docker, seeds DBs).
/// Or set ARTSYNC_INTEGRATION=true with your own SQL Server.
///
/// All tests run against artsync_src (source) and artsync_tgt (target).
///
/// After setup:
///   artsync_src has: Customers, Products, Orders, AuditLog
///   artsync_tgt has: Customers, Products, Orders  (no AuditLog)
/// </summary>
[Collection("Integration")]
public sealed class SchemaIntegrationTests
{
    private static IOperationHandler Handler() =>
        new SchemaOperationHandler(new DacFxSchemaCompare());

    private static CommandRequest Request(
        SyncMode mode,
        string? filterPath = null,
        string? syncFilePath = null) =>
        new(
            Operation:      OperationType.SchemaCompare,
            Source:         new Endpoint(EndpointKind.ConnectionString, ConnectionString: TestEnvironment.SrcCs),
            Target:         new Endpoint(EndpointKind.ConnectionString, ConnectionString: TestEnvironment.TgtCs),
            SyncMode:       mode,
            SyncFilePath:   syncFilePath,
            ArgFilePath:    null,
            CompFilePath:   null,
            FilterFilePath: filterPath,
            ReportPath:     null,
            LogPath:        null,
            ReportFormat:   null,
            Quiet:          true,
            Argv0:          "schemacompare",
            Options:        new Dictionary<string, string>
            {
                ["IgnorePermissions"]     = "yes",
                ["IgnoreUserPermissions"] = "yes",
            },
            Warnings: Array.Empty<string>()
        );

    // ── Compare only ──────────────────────────────────────────────────────────

    [IntegrationFact]
    public void CompareOnly_SrcHasExtraTable_Returns101()
    {

        // artsync_src has AuditLog; artsync_tgt does not → expect differences.
        var result = Handler().Run(Request(SyncMode.None));
        result.ExitCode.Should().Be(101);
        result.Message.Should().Contain("differences", because: "AuditLog is extra in source");
    }

    // ── Script only ───────────────────────────────────────────────────────────

    [IntegrationFact]
    public void SyncScript_ProducesCreateTableStatement()
    {

        var tmp = Path.GetTempFileName();
        try
        {
            var result = Handler().Run(Request(SyncMode.ScriptFile, syncFilePath: tmp));
            result.ExitCode.Should().Be(101, "script mode always returns 101 when diffs exist");

            var script = File.ReadAllText(tmp);
            script.Should().Contain("AuditLog",
                because: "the generated script must create the missing table");
            script.Should().NotBeEmpty();
        }
        finally { File.Delete(tmp); }
    }

    [IntegrationFact]
    public void SyncScript_DoesNotModifyTarget()
    {

        var tmp = Path.GetTempFileName();
        try
        {
            Handler().Run(Request(SyncMode.ScriptFile, syncFilePath: tmp));

            // A second compare-only should still show differences (nothing was applied).
            Handler().Run(Request(SyncMode.None)).ExitCode.Should().Be(101);
        }
        finally { File.Delete(tmp); }
    }

    // ── Apply ─────────────────────────────────────────────────────────────────

    [IntegrationFact]
    public void SyncApply_CreatesAuditLogInTarget_Returns0()
    {

        // Ensure AuditLog does not exist on target before the test.
        TestEnvironment.ExecTgt("""
            IF OBJECT_ID('dbo.AuditLog') IS NOT NULL
                DROP TABLE dbo.AuditLog;
            """);

        var result = Handler().Run(Request(SyncMode.Apply));
        result.ExitCode.Should().Be(0, "apply succeeded");

        // Verify AuditLog now exists in target.
        TestEnvironment.ExecTgt("SELECT TOP 1 * FROM dbo.AuditLog");   // should not throw

        // Clean up — drop AuditLog from target so subsequent tests start fresh.
        TestEnvironment.ExecTgt("DROP TABLE IF EXISTS dbo.AuditLog;");
    }

    // ── Identical schemas ─────────────────────────────────────────────────────

    [IntegrationFact]
    public void CompareOnly_IdenticalSchemas_Returns100()
    {

        // Create AuditLog on target so both schemas match, then compare.
        TestEnvironment.ExecTgt("""
            IF OBJECT_ID('dbo.AuditLog') IS NULL
            CREATE TABLE dbo.AuditLog (
                EventId    BIGINT        NOT NULL PRIMARY KEY IDENTITY(1,1),
                EventType  NVARCHAR(50)  NOT NULL,
                OccurredAt DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME()
            );
            """);

        try
        {
            Handler().Run(Request(SyncMode.None)).ExitCode.Should().Be(100);
        }
        finally
        {
            TestEnvironment.ExecTgt("DROP TABLE IF EXISTS dbo.AuditLog;");
        }
    }

    // ── Broken connection ─────────────────────────────────────────────────────

    [IntegrationFact]
    public void BrokenConnection_Returns40()
    {

        // 192.0.2.1 = TEST-NET-1 (RFC 5737) — guaranteed unreachable.
        // Uses SQL auth to avoid MSAL being loaded before the TCP attempt.
        var req = new CommandRequest(
            Operation:      OperationType.SchemaCompare,
            Source:         new Endpoint(EndpointKind.ConnectionString,
                                ConnectionString: "Server=192.0.2.1,1433;Database=X;" +
                                                  "User ID=sa;Password=bad;TrustServerCertificate=True;Connect Timeout=2"),
            Target:         new Endpoint(EndpointKind.ConnectionString,
                                ConnectionString: TestEnvironment.TgtCs),
            SyncMode:       SyncMode.None,
            SyncFilePath:   null,
            ArgFilePath:    null,
            CompFilePath:   null,
            FilterFilePath: null,
            ReportPath:     null,
            LogPath:        null,
            ReportFormat:   null,
            Quiet:          true,
            Argv0:          "schemacompare",
            Options:        new Dictionary<string, string>(),
            Warnings:       Array.Empty<string>()
        );

        Handler().Run(req).ExitCode.Should().Be(40);
    }
}
