using ArtSync.Abstractions;
using ArtSync.Data;
using FluentAssertions;

namespace ArtSync.Integration.Tests;

/// <summary>
/// End-to-end data compare / sync tests using live SQL Server databases.
///
/// After setup both artsync_src and artsync_tgt contain identical rows
/// in Customers, Products, and Orders — so all tests start from 100 (identical)
/// and mutate as needed.
///
/// Each test that modifies data restores state in a finally block.
/// </summary>
[Collection("Integration")]
public sealed class DataIntegrationTests
{
    private static IOperationHandler Handler() =>
        new DataOperationHandler(new SqlDataCompare());

    private static CommandRequest Request(
        SyncMode mode,
        string? syncFilePath = null,
        IReadOnlyDictionary<string, string>? options = null) =>
        new(
            Operation:      OperationType.DataCompare,
            Source:         new Endpoint(EndpointKind.ConnectionString, ConnectionString: TestEnvironment.SrcCs),
            Target:         new Endpoint(EndpointKind.ConnectionString, ConnectionString: TestEnvironment.TgtCs),
            SyncMode:       mode,
            SyncFilePath:   syncFilePath,
            ArgFilePath:    null,
            CompFilePath:   null,
            FilterFilePath: null,
            ReportPath:     null,
            LogPath:        null,
            ReportFormat:   null,
            Quiet:          true,
            Argv0:          "datacompare",
            Options:        options ?? new Dictionary<string, string>(),
            Warnings:       Array.Empty<string>()
        );

    // ── Identical data ────────────────────────────────────────────────────────

    [IntegrationFact]
    public void IdenticalData_Returns100()
    {

        // Both sides should be identical right after setup.
        Handler().Run(Request(SyncMode.None)).ExitCode.Should().Be(100);
    }

    // ── Row only in source → INSERT on apply ──────────────────────────────────

    [IntegrationFact]
    public void SourceOnlyRow_CompareOnly_Returns101()
    {

        TestEnvironment.ExecSrc("""
            SET IDENTITY_INSERT dbo.Customers ON;
            INSERT INTO dbo.Customers (CustomerId, Name, Email)
            VALUES (999, N'Test User', N'test@example.com');
            SET IDENTITY_INSERT dbo.Customers OFF;
            """);
        try
        {
            Handler().Run(Request(SyncMode.None)).ExitCode.Should().Be(101);
        }
        finally
        {
            TestEnvironment.ExecSrc("DELETE FROM dbo.Customers WHERE CustomerId = 999;");
        }
    }

    [IntegrationFact]
    public void SourceOnlyRow_SyncApply_InsertsRowInTarget_Returns0()
    {

        TestEnvironment.ExecSrc("""
            SET IDENTITY_INSERT dbo.Customers ON;
            INSERT INTO dbo.Customers (CustomerId, Name, Email)
            VALUES (998, N'Sync User', N'sync@example.com');
            SET IDENTITY_INSERT dbo.Customers OFF;
            """);
        try
        {
            var result = Handler().Run(Request(SyncMode.Apply));
            result.ExitCode.Should().Be(0, "apply should succeed");

            // Row should now exist in target.
            TestEnvironment.CountTgt("[dbo].[Customers]").Should().BeGreaterThan(3,
                because: "the new row was synced");
        }
        finally
        {
            TestEnvironment.ExecSrc("DELETE FROM dbo.Customers WHERE CustomerId = 998;");
            TestEnvironment.ExecTgt("DELETE FROM dbo.Customers WHERE CustomerId = 998;");
        }
    }

    // ── Row only in target → DELETE on apply ─────────────────────────────────

    [IntegrationFact]
    public void TargetOnlyRow_SyncApply_DeletesRowFromTarget_Returns0()
    {

        TestEnvironment.ExecTgt("""
            SET IDENTITY_INSERT dbo.Customers ON;
            INSERT INTO dbo.Customers (CustomerId, Name, Email)
            VALUES (997, N'Ghost User', NULL);
            SET IDENTITY_INSERT dbo.Customers OFF;
            """);
        try
        {
            var result = Handler().Run(Request(SyncMode.Apply));
            result.ExitCode.Should().Be(0);

            // Ghost user must be gone from target.
            TestEnvironment.ExecTgt("""
                IF EXISTS (SELECT 1 FROM dbo.Customers WHERE CustomerId = 997)
                    THROW 50001, 'Ghost row still exists', 1;
                """);
        }
        finally
        {
            // In case apply failed, clean up manually.
            TestEnvironment.ExecTgt("DELETE FROM dbo.Customers WHERE CustomerId = 997;");
        }
    }

    // ── Different row → UPDATE on apply ──────────────────────────────────────

    [IntegrationFact]
    public void DifferentRow_SyncApply_UpdatesTarget_Returns0()
    {

        // Change Bob's email in source.
        TestEnvironment.ExecSrc("""
            UPDATE dbo.Customers
            SET Email = N'bob.updated@example.com'
            WHERE CustomerId = 2;
            """);
        try
        {
            var result = Handler().Run(Request(SyncMode.Apply));
            result.ExitCode.Should().Be(0);

            // Target should now have the updated email.
            using var conn = new Microsoft.Data.SqlClient.SqlConnection(TestEnvironment.TgtCs);
            conn.Open();
            using var cmd = new Microsoft.Data.SqlClient.SqlCommand(
                "SELECT Email FROM dbo.Customers WHERE CustomerId = 2", conn);
            var email = (string?)cmd.ExecuteScalar();
            email.Should().Be("bob.updated@example.com");
        }
        finally
        {
            // Restore source and target.
            TestEnvironment.ExecSrc("UPDATE dbo.Customers SET Email = N'bob@example.com' WHERE CustomerId = 2;");
            TestEnvironment.ExecTgt("UPDATE dbo.Customers SET Email = N'bob@example.com' WHERE CustomerId = 2;");
        }
    }

    // ── Script only — does not apply ─────────────────────────────────────────

    [IntegrationFact]
    public void SyncScript_ProducesDmlFile_DoesNotModifyTarget()
    {

        TestEnvironment.ExecSrc("""
            SET IDENTITY_INSERT dbo.Products ON;
            INSERT INTO dbo.Products (ProductId, Sku, Description, Price)
            VALUES (996, N'SCRIPT-TEST', N'Script test product', 1.00);
            SET IDENTITY_INSERT dbo.Products OFF;
            """);
        var tmp = Path.GetTempFileName();
        try
        {
            var result = Handler().Run(Request(SyncMode.ScriptFile, syncFilePath: tmp));
            result.ExitCode.Should().Be(101);

            var script = File.ReadAllText(tmp);
            script.Should().Contain("SCRIPT-TEST",
                because: "the generated script should insert the new product");

            // Target must NOT have the row (script mode does not apply).
            TestEnvironment.CountTgt("[dbo].[Products]").Should().Be(3,
                because: "script mode must not modify the target");
        }
        finally
        {
            TestEnvironment.ExecSrc("DELETE FROM dbo.Products WHERE ProductId = 996;");
            File.Delete(tmp);
        }
    }

    // ── Nothing to sync ───────────────────────────────────────────────────────

    [IntegrationFact]
    public void IdenticalData_SyncApply_Returns112()
    {

        // Both sides identical → /sync with nothing to do → 112.
        Handler().Run(Request(SyncMode.Apply)).ExitCode.Should().Be(112);
    }
}
