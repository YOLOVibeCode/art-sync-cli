using System.Text.RegularExpressions;

namespace ArtSync.Data;

/// <summary>
/// Devart <c>miobjmask</c> / <c>meobjmask</c> / <c>micolmask</c> matching.
/// <c>*</c> = any run of characters, <c>?</c> = one character. Comma/semicolon
/// separate multiple masks. Brackets in <c>[dbo].[T]</c> are ignored.
/// </summary>
internal static class SqlNameMask
{
    public static bool IsTableIncluded(
        string qualifiedName,
        IReadOnlyDictionary<string, string> options)
    {
        var include = GetMasks(options, "IncludeObjectsByMask");
        var exclude = GetMasks(options, "ExcludeObjectsByMask");
        var name = Normalize(qualifiedName);

        if (include.Count > 0 && !include.Any(m => Matches(name, m)))
            return false;
        if (exclude.Any(m => Matches(name, m)))
            return false;
        return true;
    }

    public static bool IsColumnIgnored(
        string columnName,
        IReadOnlyDictionary<string, string> options)
        => GetMasks(options, "IgnoreColumnsByMask").Any(m => Matches(columnName, m));

    internal static bool Matches(string name, string mask)
    {
        var n = Normalize(name);
        var pattern = ToRegex(mask);
        if (Regex.IsMatch(n, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return true;

        var tableOnly = n.Contains('.') ? n[(n.LastIndexOf('.') + 1)..] : n;
        return Regex.IsMatch(tableOnly, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static IReadOnlyList<string> GetMasks(
        IReadOnlyDictionary<string, string> options,
        string key)
    {
        if (!options.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
            return [];
        return raw.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string Normalize(string name)
        => name.Replace("[", "", StringComparison.Ordinal)
               .Replace("]", "", StringComparison.Ordinal);

    private static string ToRegex(string mask)
    {
        var escaped = Regex.Escape(Normalize(mask));
        return "^" + escaped.Replace("\\*", ".*", StringComparison.Ordinal)
                            .Replace("\\?", ".", StringComparison.Ordinal) + "$";
    }
}
