namespace ArtSync.Abstractions;

/// <summary>
/// Carries the outcome of a schema comparison run.
/// Callers read this to decide the exit code; they do not touch DacFx types directly.
/// </summary>
public record SchemaCompareInfo(
    bool IsIdentical,
    bool HasNoComparableObjects,
    int DifferenceCount,
    IReadOnlyList<string> DifferentObjectNames,
    IReadOnlyList<string> Messages
);

/// <summary>
/// A single compare/script/publish session.  Holds DacFx state between calls so
/// the comparison is not re-run for script generation or publish.
/// </summary>
public interface ISchemaSession : IDisposable
{
    /// <summary>
    /// Runs the schema comparison.  Must be called before <see cref="GenerateScript"/>
    /// or <see cref="Publish"/>.
    /// </summary>
    SchemaCompareInfo Compare();

    /// <summary>
    /// Generates a T-SQL sync script from the last <see cref="Compare"/> result.
    /// Returns <c>null</c> when there is nothing to script.
    /// </summary>
    string? GenerateScript();

    /// <summary>
    /// Applies the differences from the last <see cref="Compare"/> result to the
    /// live target database.
    /// </summary>
    void Publish();
}

/// <summary>
/// Factory that produces <see cref="ISchemaSession"/> instances.
/// Callers depend only on this interface; DacFx is hidden behind it.
/// </summary>
public interface ISchemaCompare
{
    ISchemaSession OpenSession(
        Endpoint source,
        Endpoint target,
        IReadOnlyDictionary<string, string> options);
}

/// <summary>
/// Strongly-typed exception thrown by <see cref="ISchemaSession"/> when the
/// source or target cannot be reached, so callers can map to exit code 40.
/// </summary>
public sealed class SchemaConnectionException : Exception
{
    public SchemaConnectionException(string message, Exception? inner = null)
        : base(message, inner) { }
}

/// <summary>
/// Thrown when a sync script or report file cannot be written (exit 106 / 107).
/// </summary>
public sealed class SchemaIoException : Exception
{
    public SchemaIoException(string message, Exception? inner = null)
        : base(message, inner) { }
}

/// <summary>
/// Thrown when a <c>.scflt</c> filter file cannot be read or is malformed XML
/// (exit code 114 per SPEC §8).
/// </summary>
public sealed class SchemaFilterException : Exception
{
    public SchemaFilterException(string message, Exception? inner = null)
        : base(message, inner) { }
}

// ── Filter model ──────────────────────────────────────────────────────────────

/// <summary>
/// One entry from a <c>.scflt</c> file, per SPEC §9.4.
/// </summary>
/// <param name="ObjectType">Object type name e.g. "Table", "StoredProcedure".</param>
/// <param name="TypeEnabled">
/// When <c>false</c> the entire object type is excluded from the comparison.
/// </param>
/// <param name="NameMask">Optional wildcard mask, <c>null</c> when absent.</param>
/// <param name="MaskIncludes">
/// <c>true</c> = objects matching <see cref="NameMask"/> are <em>included</em>;
/// <c>false</c> = they are <em>excluded</em>.
/// Only meaningful when <see cref="NameMask"/> is non-null.
/// </param>
public record ObjectFilterEntry(
    string ObjectType,
    bool TypeEnabled,
    string? NameMask,
    bool MaskIncludes);

/// <summary>
/// The fully-parsed content of a <c>.scflt</c> file.
/// </summary>
public sealed class ObjectFilterSet
{
    private readonly IReadOnlyList<ObjectFilterEntry> _entries;

    public ObjectFilterSet(IReadOnlyList<ObjectFilterEntry> entries)
        => _entries = entries;

    public IReadOnlyList<ObjectFilterEntry> Entries => _entries;

    /// <summary>
    /// Returns <c>true</c> when the named object should participate in the
    /// comparison, given the filter rules.
    /// </summary>
    /// <param name="objectType">DacFx / Devart object-type string (case-insensitive).</param>
    /// <param name="qualifiedName">Schema-qualified name e.g. "[dbo].[Orders]" or "dbo.Orders".</param>
    public bool IsIncluded(string objectType, string qualifiedName)
    {
        var typeRules = _entries
            .Where(e => e.ObjectType.Equals(objectType, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (typeRules.Count == 0)
            return true;   // no rule for this type → include by default

        // Type-level disable: any entry with TypeEnabled=false and no mask disables the type.
        var typeDisabled = typeRules.Any(e => e.NameMask is null && !e.TypeEnabled);
        if (typeDisabled) return false;

        // Name-mask rules (only for types that are enabled)
        var maskRules = typeRules.Where(e => e.NameMask is not null).ToList();
        if (maskRules.Count == 0)
            return true;

        // Apply masks in order; last matching rule wins.
        bool result = true;
        foreach (var rule in maskRules)
        {
            if (MatchesWildcard(qualifiedName, rule.NameMask!))
                result = rule.MaskIncludes;
        }
        return result;
    }

    private static bool MatchesWildcard(string name, string mask)
    {
        // Strip brackets to compare bare names consistently.
        var bare = name.Replace("[", "").Replace("]", "");
        var bareMask = mask.Replace("[", "").Replace("]", "");
        return WildcardMatch(bare, bareMask, StringComparison.OrdinalIgnoreCase);
    }

    private static bool WildcardMatch(string text, string pattern, StringComparison cmp)
    {
        // Handles * and ? wildcards.
        if (pattern == "*") return true;
        if (!pattern.Contains('*') && !pattern.Contains('?'))
            return text.Equals(pattern, cmp);

        var parts = pattern.Split('*');
        if (parts.Length == 1)
        {
            // Only ? wildcards
            if (text.Length != pattern.Length) return false;
            for (int i = 0; i < pattern.Length; i++)
                if (pattern[i] != '?' && !string.Equals(text[i].ToString(), pattern[i].ToString(), cmp))
                    return false;
            return true;
        }

        // Multiple segments separated by *
        int pos = 0;
        for (int si = 0; si < parts.Length; si++)
        {
            var segment = parts[si];
            if (segment.Length == 0)
            {
                if (si == parts.Length - 1) return true;
                continue;
            }
            var idx = text.IndexOf(segment, pos, cmp);
            if (idx < 0) return false;
            if (si == 0 && idx != 0) return false;
            pos = idx + segment.Length;
        }
        return parts[^1].Length == 0 || pos == text.Length;
    }
}

/// <summary>
/// Loads and parses a Devart <c>.scflt</c> schema filter file.
/// Implementations in <c>ArtSync.Schema</c>; callers depend only on this interface.
/// </summary>
public interface IObjectFilterLoader
{
    /// <summary>
    /// Parses the file at <paramref name="path"/> and returns the filter set.
    /// </summary>
    /// <exception cref="SchemaFilterException">File not found, unreadable, or invalid XML.</exception>
    ObjectFilterSet Load(string path);
}
