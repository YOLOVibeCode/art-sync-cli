using ArtSync.Abstractions;
using Microsoft.Data.SqlClient;

namespace ArtSync.Data;

/// <summary>
/// Executes a T-SQL sync script against the target database with exponential-backoff
/// retry on Azure SQL transient errors (SPEC §10.2 step 9).
/// </summary>
internal sealed class SqlDataApplier
{
    // Azure SQL transient error numbers (SPEC §10.2).
    private static readonly HashSet<int> TransientErrors = new()
        { 40613, 40197, 40501, 10928, 10929, 10053, 10054, 10060, 233, 64 };

    private const int MaxRetries     = 5;
    private const int BaseDelayMs    = 500;
    private const int MaxDelayMs     = 30_000;

    public void Apply(string connectionString, string script, IReadOnlyDictionary<string, string> options)
    {
        if (string.IsNullOrWhiteSpace(script)) return;

        var batches = SplitBatches(script);
        bool useTran = SqlOptionFlags.IsOn(options, "ExecuteAsSingleTransaction", defaultOn: true);

        int attempt = 0;
        while (true)
        {
            try
            {
                if (useTran)
                    ExecuteBatches(connectionString, batches, transactional: true);
                else
                    ExecuteBatches(connectionString, batches, transactional: false);
                return;
            }
            catch (SqlException ex) when (IsTransient(ex) && attempt < MaxRetries)
            {
                attempt++;
                var delay = Math.Min(BaseDelayMs * (int)Math.Pow(2, attempt - 1), MaxDelayMs);
                Thread.Sleep(delay);
            }
        }
    }

    private static void ExecuteBatches(string cs, IReadOnlyList<string> batches, bool transactional)
    {
        using var conn = new SqlConnection(cs);
        conn.Open();
        SqlTransaction? tx = transactional ? conn.BeginTransaction() : null;
        try
        {
            foreach (var batch in batches)
            {
                if (string.IsNullOrWhiteSpace(batch)) continue;
                using var cmd = tx is null
                    ? new SqlCommand(batch, conn)
                    : new SqlCommand(batch, conn, tx);
                cmd.CommandTimeout = 300;
                cmd.ExecuteNonQuery();
            }
            tx?.Commit();
        }
        catch
        {
            tx?.Rollback();
            throw;
        }
    }

    private static bool IsTransient(SqlException ex)
    {
        if (ex.IsTransient) return true;
        foreach (SqlError err in ex.Errors)
            if (TransientErrors.Contains(err.Number))
                return true;
        return false;
    }

    private static IReadOnlyList<string> SplitBatches(string script)
    {
        // Split on lines containing only "GO" (case-insensitive).
        var batches = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (var line in script.Split('\n'))
        {
            var trimmed = line.TrimEnd();
            if (trimmed.Equals("GO", StringComparison.OrdinalIgnoreCase))
            {
                batches.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.AppendLine(trimmed);
            }
        }

        if (current.Length > 0)
            batches.Add(current.ToString());

        return batches;
    }
}
