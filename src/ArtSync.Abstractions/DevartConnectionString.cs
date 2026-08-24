namespace ArtSync.Abstractions;

/// <summary>
/// Devart <c>connection:"…"</c> strings include keywords Microsoft.Data.SqlClient
/// rejects (notably <c>Transaction Scope Local</c>). Azure SQL also refuses
/// <c>Encrypt=False</c>, which Devart still emits.
/// </summary>
public static class DevartConnectionString
{
    private static readonly HashSet<string> SqlClientUnsupported = new(StringComparer.OrdinalIgnoreCase)
    {
        "Transaction Scope Local",
    };

    public static string ForSqlClient(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("Connection string is empty.");

        var pairs = new List<(string Key, string Value)>();
        string? dataSource = null;

        foreach (var (key, value) in SplitPairs(Unquote(raw.Trim())))
        {
            if (SqlClientUnsupported.Contains(key))
                continue;

            if (IsDataSourceKey(key))
                dataSource = value;

            pairs.Add((key, value));
        }

        if (IsAzureSql(dataSource))
        {
            pairs.RemoveAll(p => p.Key.Equals("Encrypt", StringComparison.OrdinalIgnoreCase));
            pairs.Add(("Encrypt", "True"));
        }

        return string.Join(";", pairs.Select(p => $"{p.Key}={p.Value}"));
    }

    internal static IEnumerable<(string Key, string Value)> SplitPairs(string connectionString)
    {
        foreach (var segment in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = segment.IndexOf('=');
            if (eq <= 0)
                continue;
            yield return (segment[..eq].Trim(), segment[(eq + 1)..].Trim());
        }
    }

    internal static string Unquote(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            return value[1..^1];
        return value;
    }

    private static bool IsDataSourceKey(string key)
        => key.Equals("Data Source", StringComparison.OrdinalIgnoreCase)
        || key.Equals("Server", StringComparison.OrdinalIgnoreCase)
        || key.Equals("Addr", StringComparison.OrdinalIgnoreCase)
        || key.Equals("Address", StringComparison.OrdinalIgnoreCase)
        || key.Equals("Network Address", StringComparison.OrdinalIgnoreCase);

    private static bool IsAzureSql(string? dataSource)
        => dataSource is not null
        && dataSource.Contains(".database.windows.net", StringComparison.OrdinalIgnoreCase);
}
