using Microsoft.Data.SqlClient;

namespace ArtSync.Integration.Tests;

/// <summary>
/// xUnit collection fixture: restores both test databases to a known-good identical
/// baseline before the Integration test collection starts.
///
/// Runs the lightweight "reset" SQL that truncates and re-seeds all tables with the
/// same rows that setup.sql creates, without dropping/recreating databases (fast).
/// </summary>
[CollectionDefinition("Integration")]
public sealed class IntegrationCollection : ICollectionFixture<DatabaseResetFixture> { }

public sealed class DatabaseResetFixture : IDisposable
{
    public DatabaseResetFixture()
    {
        if (!TestEnvironment.IsEnabled) return;
        ResetToBaseline();
    }

    public void Dispose() { }

    // ── Baseline reset ────────────────────────────────────────────────────────

    private static void ResetToBaseline()
    {
        // ── Source ────────────────────────────────────────────────────────────
        ExecSrc("""
            -- Customers (with explicit CreatedAt so hash matches target)
            DELETE FROM dbo.Orders;
            DELETE FROM dbo.AuditLog;
            DELETE FROM dbo.Customers;
            SET IDENTITY_INSERT dbo.Customers ON;
            INSERT INTO dbo.Customers (CustomerId, Name, Email, CreatedAt) VALUES
                (1, N'Alice Turing',  N'alice@example.com', '2026-01-01 00:00:00.0000000'),
                (2, N'Bob Lovelace',  N'bob@example.com',   '2026-01-01 00:00:00.0000000'),
                (3, N'Carol Shannon', NULL,                  '2026-01-01 00:00:00.0000000');
            SET IDENTITY_INSERT dbo.Customers OFF;

            -- Products
            DELETE FROM dbo.Products;
            SET IDENTITY_INSERT dbo.Products ON;
            INSERT INTO dbo.Products (ProductId, Sku, Description, Price) VALUES
                (1, N'WIDGET-A', N'Standard Widget', 9.99),
                (2, N'WIDGET-B', N'Premium Widget',  24.99),
                (3, N'GADGET-X', NULL,               49.99);
            SET IDENTITY_INSERT dbo.Products OFF;

            -- Orders
            SET IDENTITY_INSERT dbo.Orders ON;
            INSERT INTO dbo.Orders (OrderId, CustomerId, OrderDate, TotalAmount) VALUES
                (1, 1, '2026-01-10', 9.99),
                (2, 1, '2026-02-14', 34.98),
                (3, 2, '2026-01-20', 49.99);
            SET IDENTITY_INSERT dbo.Orders OFF;

            -- TypeSampler: reset to canonical row
            DELETE FROM dbo.TypeSampler;
            SET IDENTITY_INSERT dbo.TypeSampler ON;
            INSERT INTO dbo.TypeSampler (
                SamplerId,
                ColBigInt, ColSmallInt, ColTinyInt, ColBit,
                ColDecimal, ColNumeric, ColMoney, ColSmallMoney, ColFloat, ColReal,
                ColDate, ColTime, ColDateTime, ColDateTime2, ColSmallDateTime, ColDateTimeOffset,
                ColChar, ColNChar, ColVarChar, ColNVarChar,
                ColBinary, ColVarBinary, ColGuid
            ) VALUES (
                1,
                9223372036854775807, 32767, 255, 1,
                12345678.123456, 9999.99, 99999.9900, 214.7483, 3.14159265358979, CAST(2.71828 AS REAL),
                '2026-08-13', '19:30:00.1234567', '2026-08-13 19:30:00.000', '2026-08-13 19:30:00.1234567',
                '2026-08-13 19:30:00', '2026-08-13 19:30:00.1234567 +05:30',
                'CHAR      ', N'NCHAR     ', 'varchar value', N'nvarchar value — ñoño',
                0x0102030405060708, 0xDEADBEEF01020304,
                'A0EEBC99-9C0B-4EF8-BB6D-6BB9BD380A11'
            );
            SET IDENTITY_INSERT dbo.TypeSampler OFF;
            """);

        // ── Target (same data, same explicit values) ──────────────────────────
        ExecTgt("""
            DELETE FROM dbo.Orders;
            DELETE FROM dbo.Customers;

            SET IDENTITY_INSERT dbo.Customers ON;
            INSERT INTO dbo.Customers (CustomerId, Name, Email, CreatedAt) VALUES
                (1, N'Alice Turing',  N'alice@example.com', '2026-01-01 00:00:00.0000000'),
                (2, N'Bob Lovelace',  N'bob@example.com',   '2026-01-01 00:00:00.0000000'),
                (3, N'Carol Shannon', NULL,                  '2026-01-01 00:00:00.0000000');
            SET IDENTITY_INSERT dbo.Customers OFF;

            DELETE FROM dbo.Products;
            SET IDENTITY_INSERT dbo.Products ON;
            INSERT INTO dbo.Products (ProductId, Sku, Description, Price) VALUES
                (1, N'WIDGET-A', N'Standard Widget', 9.99),
                (2, N'WIDGET-B', N'Premium Widget',  24.99),
                (3, N'GADGET-X', NULL,               49.99);
            SET IDENTITY_INSERT dbo.Products OFF;

            SET IDENTITY_INSERT dbo.Orders ON;
            INSERT INTO dbo.Orders (OrderId, CustomerId, OrderDate, TotalAmount) VALUES
                (1, 1, '2026-01-10', 9.99),
                (2, 1, '2026-02-14', 34.98),
                (3, 2, '2026-01-20', 49.99);
            SET IDENTITY_INSERT dbo.Orders OFF;

            -- TypeSampler: reset to same canonical row as source
            DELETE FROM dbo.TypeSampler;
            SET IDENTITY_INSERT dbo.TypeSampler ON;
            INSERT INTO dbo.TypeSampler (
                SamplerId,
                ColBigInt, ColSmallInt, ColTinyInt, ColBit,
                ColDecimal, ColNumeric, ColMoney, ColSmallMoney, ColFloat, ColReal,
                ColDate, ColTime, ColDateTime, ColDateTime2, ColSmallDateTime, ColDateTimeOffset,
                ColChar, ColNChar, ColVarChar, ColNVarChar,
                ColBinary, ColVarBinary, ColGuid
            ) VALUES (
                1,
                9223372036854775807, 32767, 255, 1,
                12345678.123456, 9999.99, 99999.9900, 214.7483, 3.14159265358979, CAST(2.71828 AS REAL),
                '2026-08-13', '19:30:00.1234567', '2026-08-13 19:30:00.000', '2026-08-13 19:30:00.1234567',
                '2026-08-13 19:30:00', '2026-08-13 19:30:00.1234567 +05:30',
                'CHAR      ', N'NCHAR     ', 'varchar value', N'nvarchar value — ñoño',
                0x0102030405060708, 0xDEADBEEF01020304,
                'A0EEBC99-9C0B-4EF8-BB6D-6BB9BD380A11'
            );
            SET IDENTITY_INSERT dbo.TypeSampler OFF;
            """);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void ExecSrc(string sql) => ExecBatch(TestEnvironment.SrcCs, sql);
    private static void ExecTgt(string sql) => ExecBatch(TestEnvironment.TgtCs, sql);

    private static void ExecBatch(string cs, string sql)
    {
        using var conn = new SqlConnection(cs);
        conn.Open();
        // Simple single-batch execution (no GO separators in the SQL above).
        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 60 };
        cmd.ExecuteNonQuery();
    }
}
