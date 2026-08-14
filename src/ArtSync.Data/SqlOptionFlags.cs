namespace ArtSync.Data;

/// <summary>Devart boolean vocabulary plus documented defaults for data-sync options.</summary>
internal static class SqlOptionFlags
{
    public static bool IsOn(
        IReadOnlyDictionary<string, string> opts,
        string key,
        bool defaultOn = false)
    {
        if (!opts.TryGetValue(key, out var v) || string.IsNullOrWhiteSpace(v))
            return defaultOn;

        return v.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || v.Equals("y", StringComparison.OrdinalIgnoreCase)
            || v.Equals("on", StringComparison.OrdinalIgnoreCase)
            || v.Equals("true", StringComparison.OrdinalIgnoreCase)
            || v.Equals("t", StringComparison.OrdinalIgnoreCase);
    }
}
