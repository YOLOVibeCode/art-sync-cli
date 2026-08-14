using Microsoft.Data.SqlClient;

namespace ArtSync.Integration.Tests;

/// <summary>
/// Reads integration-test settings from environment variables and provides
/// helpers for resetting the test databases between tests.
///
/// Tests are unconditionally skipped unless ARTSYNC_INTEGRATION=true is set.
///
/// Default connection strings target a Docker SQL Server started by
/// docker-compose.yml (see tests/fixtures/README.md).
/// </summary>
public static class TestEnvironment
{
    private const string DefaultSrcCs =
        "Server=localhost,1433;Database=artsync_src;User ID=sa;" +
        "Password=ArtSync_Test@2026;TrustServerCertificate=True;Encrypt=True";

    private const string DefaultTgtCs =
        "Server=localhost,1433;Database=artsync_tgt;User ID=sa;" +
        "Password=ArtSync_Test@2026;TrustServerCertificate=True;Encrypt=True";

    public static bool IsEnabled =>
        string.Equals(
            Environment.GetEnvironmentVariable("ARTSYNC_INTEGRATION"),
            "true", StringComparison.OrdinalIgnoreCase);

    public const string SkipReason =
        "Integration tests require a SQL Server. " +
        "Set ARTSYNC_INTEGRATION=true and run ./scripts/run-integration-tests.sh";

    public static string SrcCs =>
        Environment.GetEnvironmentVariable("ARTSYNC_SRC_CS") ?? DefaultSrcCs;

    public static string TgtCs =>
        Environment.GetEnvironmentVariable("ARTSYNC_TGT_CS") ?? DefaultTgtCs;

    /// <summary>
    /// Executes arbitrary T-SQL against the target database (for test setup/teardown).
    /// </summary>
    public static void ExecTgt(string sql)
    {
        using var conn = new SqlConnection(TgtCs);
        conn.Open();
        using var cmd = new SqlCommand(sql, conn);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Executes arbitrary T-SQL against the source database.
    /// </summary>
    public static void ExecSrc(string sql)
    {
        using var conn = new SqlConnection(SrcCs);
        conn.Open();
        using var cmd = new SqlCommand(sql, conn);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Counts rows in a table using the target connection.</summary>
    public static int CountTgt(string qualifiedTable)
    {
        using var conn = new SqlConnection(TgtCs);
        conn.Open();
        using var cmd = new SqlCommand($"SELECT COUNT(*) FROM {qualifiedTable}", conn);
        return (int)cmd.ExecuteScalar()!;
    }

    /// <summary>Counts rows in a table using the source connection.</summary>
    public static int CountSrc(string qualifiedTable)
    {
        using var conn = new SqlConnection(SrcCs);
        conn.Open();
        using var cmd = new SqlCommand($"SELECT COUNT(*) FROM {qualifiedTable}", conn);
        return (int)cmd.ExecuteScalar()!;
    }
}
