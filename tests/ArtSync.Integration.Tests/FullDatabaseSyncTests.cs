using ArtSync.Abstractions;
using ArtSync.Data;
using ArtSync.Schema;
using FluentAssertions;
using Microsoft.Data.SqlClient;

namespace ArtSync.Integration.Tests;

/// <summary>
/// Realistic source→target synchronization: FKs, composite keys, GUID keys,
/// unique-constraint tables, NULL vs empty, quotes, schema-then-data.
/// </summary>
[Collection("Integration")]
public sealed class FullDatabaseSyncTests : IDisposable
{
    public FullDatabaseSyncTests()
    {
        if (TestEnvironment.IsEnabled)
            DatabaseResetFixture.ResetToBaseline();
    }

    public void Dispose()
    {
        if (TestEnvironment.IsEnabled)
            DatabaseResetFixture.ResetToBaseline();
    }

    private static IOperationHandler Data() => new DataOperationHandler(new SqlDataCompare());
    private static IOperationHandler Schema() => new SchemaOperationHandler(new DacFxSchemaCompare());

    private static readonly Dictionary<string, string> EnforceFks = new()
    {
        ["DisableForeignKeys"] = "no",
        ["DisableDmlTriggers"] = "no",
    };

    private static CommandRequest DataRequest(
        SyncMode mode,
        IReadOnlyDictionary<string, string>? options = null) =>
        new(
            Operation:      OperationType.DataCompare,
            Source:         new Endpoint(EndpointKind.ConnectionString, ConnectionString: TestEnvironment.SrcCs),
            Target:         new Endpoint(EndpointKind.ConnectionString, ConnectionString: TestEnvironment.TgtCs),
            SyncMode:       mode,
            SyncFilePath:   null,
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

    private static CommandRequest SchemaRequest(SyncMode mode) =>
        new(
            Operation:      OperationType.SchemaCompare,
            Source:         new Endpoint(EndpointKind.ConnectionString, ConnectionString: TestEnvironment.SrcCs),
            Target:         new Endpoint(EndpointKind.ConnectionString, ConnectionString: TestEnvironment.TgtCs),
            SyncMode:       mode,
            SyncFilePath:   null,
            ArgFilePath:    null,
            CompFilePath:   null,
            FilterFilePath: null,
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
            Warnings:       Array.Empty<string>()
        );

    // ── FK order with constraints left enabled ────────────────────────────────

    [IntegrationFact]
    public void ParentAndChild_InsertTogether_WithFksEnforced_Returns0()
    {
        TestEnvironment.ExecSrc("""
            SET IDENTITY_INSERT dbo.Customers ON;
            INSERT INTO dbo.Customers (CustomerId, Name, Email, CreatedAt)
            VALUES (800, N'New Parent', N'parent@example.com', '2026-01-01');
            SET IDENTITY_INSERT dbo.Customers OFF;

            SET IDENTITY_INSERT dbo.Orders ON;
            INSERT INTO dbo.Orders (OrderId, CustomerId, OrderDate, TotalAmount)
            VALUES (800, 800, '2026-08-01', 10.00);
            SET IDENTITY_INSERT dbo.Orders OFF;

            INSERT INTO dbo.OrderLines (OrderId, [LineNo], Sku, Qty)
            VALUES (800, 1, N'WIDGET-A', 2);
            """);

        var result = Data().Run(DataRequest(SyncMode.Apply, EnforceFks));
        result.ExitCode.Should().Be(0, result.Message);

        ScalarTgt("SELECT Name FROM dbo.Customers WHERE CustomerId = 800")
            .Should().Be("New Parent");
        ScalarTgt("SELECT COUNT(*) FROM dbo.Orders WHERE OrderId = 800")
            .Should().Be(1);
        ScalarTgt("SELECT Qty FROM dbo.OrderLines WHERE OrderId = 800 AND [LineNo] = 1")
            .Should().Be(2);
    }

    [IntegrationFact]
    public void ParentAndChild_DeleteTogether_WithFksEnforced_Returns0()
    {
        TestEnvironment.ExecSrc("""
            SET IDENTITY_INSERT dbo.Customers ON;
            INSERT INTO dbo.Customers (CustomerId, Name, Email, CreatedAt)
            VALUES (801, N'Doomed', NULL, '2026-01-01');
            SET IDENTITY_INSERT dbo.Customers OFF;
            SET IDENTITY_INSERT dbo.Orders ON;
            INSERT INTO dbo.Orders (OrderId, CustomerId, OrderDate, TotalAmount)
            VALUES (801, 801, '2026-08-01', 1.00);
            SET IDENTITY_INSERT dbo.Orders OFF;
            INSERT INTO dbo.OrderLines (OrderId, [LineNo], Sku, Qty) VALUES (801, 1, N'WIDGET-A', 1);
            """);
        TestEnvironment.ExecTgt("""
            SET IDENTITY_INSERT dbo.Customers ON;
            INSERT INTO dbo.Customers (CustomerId, Name, Email, CreatedAt)
            VALUES (801, N'Doomed', NULL, '2026-01-01');
            SET IDENTITY_INSERT dbo.Customers OFF;
            SET IDENTITY_INSERT dbo.Orders ON;
            INSERT INTO dbo.Orders (OrderId, CustomerId, OrderDate, TotalAmount)
            VALUES (801, 801, '2026-08-01', 1.00);
            SET IDENTITY_INSERT dbo.Orders OFF;
            INSERT INTO dbo.OrderLines (OrderId, [LineNo], Sku, Qty) VALUES (801, 1, N'WIDGET-A', 1);
            """);

        TestEnvironment.ExecSrc("""
            DELETE FROM dbo.OrderLines WHERE OrderId = 801;
            DELETE FROM dbo.Orders WHERE OrderId = 801;
            DELETE FROM dbo.Customers WHERE CustomerId = 801;
            """);

        var result = Data().Run(DataRequest(SyncMode.Apply, EnforceFks));
        result.ExitCode.Should().Be(0, result.Message);

        ScalarTgt("SELECT COUNT(*) FROM dbo.Customers WHERE CustomerId = 801").Should().Be(0);
        ScalarTgt("SELECT COUNT(*) FROM dbo.Orders WHERE OrderId = 801").Should().Be(0);
        ScalarTgt("SELECT COUNT(*) FROM dbo.OrderLines WHERE OrderId = 801").Should().Be(0);
    }

    // ── NULL vs empty, quotes, GUID / unique / composite keys ─────────────────

    [IntegrationFact]
    public void NullVersusEmptyString_IsARealDifference_AndSyncsToNull()
    {
        TestEnvironment.ExecTgt("UPDATE dbo.Customers SET Email = N'' WHERE CustomerId = 3;");

        Data().Run(DataRequest(SyncMode.None)).ExitCode.Should().Be(101,
            "NULL and empty string must not hash as equal by default");

        Data().Run(DataRequest(SyncMode.Apply)).ExitCode.Should().Be(0);

        ScalarTgt("SELECT Email FROM dbo.Customers WHERE CustomerId = 3").Should().BeNull();
    }

    [IntegrationFact]
    public void ApostropheInNvarchar_RoundTrips()
    {
        TestEnvironment.ExecSrc("UPDATE dbo.Customers SET Name = N'O''Brien' WHERE CustomerId = 1;");

        Data().Run(DataRequest(SyncMode.Apply)).ExitCode.Should().Be(0);
        ScalarTgt("SELECT Name FROM dbo.Customers WHERE CustomerId = 1").Should().Be("O'Brien");
    }

    [IntegrationFact]
    public void GuidPrimaryKey_InsertsOnTarget()
    {
        TestEnvironment.ExecSrc("""
            INSERT INTO dbo.GuidKeys (Id, Label)
            VALUES ('22222222-2222-2222-2222-222222222222', N'bravo');
            """);

        Data().Run(DataRequest(SyncMode.Apply)).ExitCode.Should().Be(0);
        ScalarTgt("SELECT Label FROM dbo.GuidKeys WHERE Id = '22222222-2222-2222-2222-222222222222'")
            .Should().Be("bravo");
    }

    [IntegrationFact]
    public void CompositePrimaryKey_InsertsOnTarget()
    {
        TestEnvironment.ExecSrc("""
            INSERT INTO dbo.OrderLines (OrderId, [LineNo], Sku, Qty)
            VALUES (1, 2, N'WIDGET-B', 9);
            """);

        Data().Run(DataRequest(SyncMode.Apply, EnforceFks)).ExitCode.Should().Be(0);
        ScalarTgt("SELECT Qty FROM dbo.OrderLines WHERE OrderId = 1 AND [LineNo] = 2").Should().Be(9);
    }

    [IntegrationFact]
    public void UniqueConstraintTable_WithoutPrimaryKey_StillSyncs()
    {
        TestEnvironment.ExecSrc("INSERT INTO dbo.Settings (SettingKey, SettingValue) VALUES (N'Locale', N'en-GB');");

        Data().Run(DataRequest(SyncMode.Apply)).ExitCode.Should().Be(0);
        ScalarTgt("SELECT SettingValue FROM dbo.Settings WHERE SettingKey = N'Locale'").Should().Be("en-GB");
    }

    [IntegrationFact]
    public void HeapWithoutKey_IsSkipped_AndDoesNotBlockIdentical()
    {
        TestEnvironment.ExecSrc("INSERT INTO dbo.HeapEvents (Note) VALUES (N'source-only heap row');");

        Data().Run(DataRequest(SyncMode.None)).ExitCode.Should().Be(100,
            "heaps without a usable key must be skipped, not compared");
        TestEnvironment.CountTgt("[dbo].[HeapEvents]").Should().Be(1,
            "the extra heap row must not have been copied");
    }

    [IntegrationFact]
    public void ExcludeObjectMask_IgnoresMaskedTable()
    {
        TestEnvironment.ExecSrc("UPDATE dbo.TypeSampler SET ColBit = 0 WHERE SamplerId = 1;");

        var opts = new Dictionary<string, string> { ["ExcludeObjectsByMask"] = "TypeSampler*" };
        Data().Run(DataRequest(SyncMode.None, opts)).ExitCode.Should().Be(100);
    }

    // ── Mixed mutations in one apply ──────────────────────────────────────────

    [IntegrationFact]
    public void MixedInsertUpdateDeleteAcrossFkGraph_EndsWithMatchingData()
    {
        TestEnvironment.ExecSrc("""
            UPDATE dbo.Customers SET Email = N'alice.updated@example.com' WHERE CustomerId = 1;
            SET IDENTITY_INSERT dbo.Customers ON;
            INSERT INTO dbo.Customers (CustomerId, Name, Email, CreatedAt)
            VALUES (900, N'Mixed Parent', N'mixed@example.com', '2026-01-01');
            SET IDENTITY_INSERT dbo.Customers OFF;
            SET IDENTITY_INSERT dbo.Orders ON;
            INSERT INTO dbo.Orders (OrderId, CustomerId, OrderDate, TotalAmount)
            VALUES (900, 900, '2026-08-13', 5.00);
            SET IDENTITY_INSERT dbo.Orders OFF;
            INSERT INTO dbo.OrderLines (OrderId, [LineNo], Sku, Qty) VALUES (900, 1, N'GADGET-X', 3);
            INSERT INTO dbo.Settings (SettingKey, SettingValue) VALUES (N'Mixed', N'yes');
            """);
        TestEnvironment.ExecTgt("""
            SET IDENTITY_INSERT dbo.Customers ON;
            INSERT INTO dbo.Customers (CustomerId, Name, Email, CreatedAt)
            VALUES (901, N'TargetOnly', NULL, '2026-01-01');
            SET IDENTITY_INSERT dbo.Customers OFF;
            """);

        var result = Data().Run(DataRequest(SyncMode.Apply));
        result.ExitCode.Should().Be(0, result.Message);

        Data().Run(DataRequest(SyncMode.None)).ExitCode.Should().Be(100);
        ScalarTgt("SELECT Email FROM dbo.Customers WHERE CustomerId = 1")
            .Should().Be("alice.updated@example.com");
        ScalarTgt("SELECT COUNT(*) FROM dbo.Customers WHERE CustomerId = 900").Should().Be(1);
        ScalarTgt("SELECT COUNT(*) FROM dbo.Customers WHERE CustomerId = 901").Should().Be(0);
        ScalarTgt("SELECT Qty FROM dbo.OrderLines WHERE OrderId = 900 AND [LineNo] = 1").Should().Be(3);
    }

    // ── Schema then data ──────────────────────────────────────────────────────

    [IntegrationFact]
    public void SchemaApplyThenDataApply_TargetMatchesSource()
    {
        TestEnvironment.ExecTgt("IF OBJECT_ID('dbo.AuditLog') IS NOT NULL DROP TABLE dbo.AuditLog;");

        var schema = Schema().Run(SchemaRequest(SyncMode.Apply));
        schema.ExitCode.Should().Be(0, schema.Message);

        var data = Data().Run(DataRequest(SyncMode.Apply));
        data.ExitCode.Should().BeOneOf([0, 112], data.Message);

        Schema().Run(SchemaRequest(SyncMode.None)).ExitCode.Should().Be(100);
        Data().Run(DataRequest(SyncMode.None)).ExitCode.Should().Be(100);
    }

    private static object? ScalarTgt(string sql)
    {
        using var conn = new SqlConnection(TestEnvironment.TgtCs);
        conn.Open();
        using var cmd = new SqlCommand(sql, conn);
        var v = cmd.ExecuteScalar();
        return v is DBNull ? null : v;
    }
}
