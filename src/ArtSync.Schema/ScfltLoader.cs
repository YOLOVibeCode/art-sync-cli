using System.Xml.Linq;
using ArtSync.Abstractions;

namespace ArtSync.Schema;

/// <summary>
/// Parses Devart <c>.scflt</c> schema filter XML files (SPEC §9.4).
///
/// Supported formats:
/// <list type="bullet">
///   <item>Attribute form:
///     <code>&lt;FiltersCollection&gt;
///   &lt;Filter ObjectName="Table" Checked="True" Filter="dbo.Audit*" Include="False" /&gt;
/// &lt;/FiltersCollection&gt;</code></item>
///   <item>Element / sub-filter form:
///     <code>&lt;ArrayOfSchemaFilter&gt;
///   &lt;SchemaFilter&gt;
///     &lt;ObjectName&gt;Table&lt;/ObjectName&gt;
///     &lt;Checked&gt;true&lt;/Checked&gt;
///     &lt;SubFilters&gt;
///       &lt;SchemaSubFilter&gt;
///         &lt;Filter&gt;dbo.Audit*&lt;/Filter&gt;
///         &lt;Include&gt;false&lt;/Include&gt;
///       &lt;/SchemaSubFilter&gt;
///     &lt;/SubFilters&gt;
///   &lt;/SchemaFilter&gt;
/// &lt;/ArrayOfSchemaFilter&gt;</code></item>
/// </list>
/// </summary>
public sealed class ScfltLoader : IObjectFilterLoader
{
    public ObjectFilterSet Load(string path)
    {
        if (!File.Exists(path))
            throw new SchemaFilterException($"Filter file not found: '{path}'");

        string xml;
        try { xml = File.ReadAllText(path); }
        catch (Exception ex)
        {
            throw new SchemaFilterException(
                $"Cannot read filter file '{path}': {ex.Message}", ex);
        }

        XDocument doc;
        try { doc = XDocument.Parse(xml); }
        catch (Exception ex)
        {
            throw new SchemaFilterException(
                $"Filter file '{path}' is not valid XML: {ex.Message}", ex);
        }

        if (doc.Root is null)
            throw new SchemaFilterException(
                $"Filter file '{path}' has no root element.");

        var entries = new List<ObjectFilterEntry>();
        ParseRoot(doc.Root, entries);

        if (entries.Count == 0)
            throw new SchemaFilterException(
                $"Filter file '{path}' contains no recognisable filter entries. " +
                "Expected elements with 'ObjectName' attribute or child element.");

        return new ObjectFilterSet(entries);
    }

    // ── Recursive parser ──────────────────────────────────────────────────────

    private static void ParseRoot(XElement root, List<ObjectFilterEntry> results)
    {
        foreach (var el in root.Elements())
        {
            var objectType = AttrOrElem(el, "ObjectName");
            if (objectType is null)
            {
                // May be a nesting wrapper; descend one more level.
                ParseRoot(el, results);
                continue;
            }

            ParseFilterElement(el, objectType, results);
        }
    }

    private static void ParseFilterElement(
        XElement el, string objectType, List<ObjectFilterEntry> results)
    {
        var checkedStr = AttrOrElem(el, "Checked") ?? "true";
        bool typeEnabled = ParseBool(checkedStr, defaultValue: true);

        // Direct name mask on the element itself
        var nameMask = AttrOrElem(el, "Filter")
                    ?? AttrOrElem(el, "NameMask")
                    ?? AttrOrElem(el, "Mask");

        var includeStr = AttrOrElem(el, "Include") ?? "true";
        bool maskIncludes = ParseBool(includeStr, defaultValue: true);

        // Emit the type-level entry (controls whether the type is on at all).
        results.Add(new ObjectFilterEntry(objectType, typeEnabled, nameMask, maskIncludes));

        // Sub-filters: <SubFilters><SchemaSubFilter><Filter>…</Filter><Include>…</Include>
        var subFilters = el.Element("SubFilters");
        if (subFilters is not null)
        {
            foreach (var sub in subFilters.Elements())
            {
                var subMask = AttrOrElem(sub, "Filter")
                           ?? AttrOrElem(sub, "NameMask")
                           ?? AttrOrElem(sub, "Mask");
                if (subMask is null) continue;

                var subIncStr = AttrOrElem(sub, "Include") ?? "true";
                bool subIncludes = ParseBool(subIncStr, defaultValue: true);

                results.Add(new ObjectFilterEntry(objectType, TypeEnabled: true, subMask, subIncludes));
            }
        }
    }

    // ── Attribute / element helpers ───────────────────────────────────────────

    private static string? AttrOrElem(XElement el, string name)
    {
        var attr = el.Attribute(name);
        if (attr is not null && !string.IsNullOrWhiteSpace(attr.Value))
            return attr.Value.Trim();

        var child = el.Element(name);
        if (child is not null && !string.IsNullOrWhiteSpace(child.Value))
            return child.Value.Trim();

        return null;
    }

    private static bool ParseBool(string value, bool defaultValue) =>
        value.Trim().ToLowerInvariant() switch
        {
            "true" or "yes" or "y" or "on" or "1" => true,
            "false" or "no" or "n" or "off" or "0" => false,
            _ => defaultValue,
        };
}
