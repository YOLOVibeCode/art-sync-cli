using ArtSync.Abstractions;

namespace ArtSync.Compat;

/// <summary>
/// Thrown while loading or merging a Devart <c>/compfile</c>.
/// <see cref="ExitCode"/> is 10 (missing/unsupported) or 30 (corrupt).
/// </summary>
public sealed class CompFileException : Exception
{
    public int ExitCode { get; }

    public CompFileException(int exitCode, string message, Exception? inner = null)
        : base(message, inner)
        => ExitCode = exitCode;
}

/// <summary>Parsed Devart data-compare project (<c>.dcomp</c>).</summary>
public sealed class CompFileDocument
{
    public Endpoint? Source { get; init; }
    public Endpoint? Target { get; init; }
    public IReadOnlyDictionary<string, string> Options { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<string> ExcludedObjectNames { get; init; } = [];
    /// <summary>
    /// Mapping objects with <c>Included</c> true (or omitted). When non-empty,
    /// these are the Devart checkbox allowlist — compare only these tables.
    /// </summary>
    public IReadOnlyList<string> IncludedObjectNames { get; init; } = [];
}

/// <summary>
/// Loads Devart <c>.dcomp</c> XML captured from dbForge Data Compare
/// (provider 5.x <c>ConnectionString</c> element and 6.x <c>ConnectionModel</c>).
/// </summary>
public static class CompFileParser
{
    public static CompFileDocument Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new CompFileException(10, "/compfile requires a path: /compfile:<path>");

        if (!File.Exists(path))
            throw new CompFileException(10, $"/compfile: file not found: {path}");

        string xml;
        try { xml = File.ReadAllText(path); }
        catch (Exception ex)
        {
            throw new CompFileException(30, $"/compfile: cannot read '{path}': {ex.Message}", ex);
        }

        try
        {
            return ParseXml(xml, path);
        }
        catch (CompFileException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new CompFileException(30,
                $"/compfile '{path}' is not a valid Devart data-compare project: {ex.Message}", ex);
        }
    }

    internal static CompFileDocument ParseXml(string xml, string pathForErrors)
    {
        var doc = System.Xml.Linq.XDocument.Parse(xml);
        var root = doc.Root ?? throw new CompFileException(30,
            $"/compfile '{pathForErrors}' has no root element.");

        if (!root.Name.LocalName.Equals("CompareDocument", StringComparison.OrdinalIgnoreCase))
            throw new CompFileException(30,
                $"/compfile '{pathForErrors}' is not a Devart CompareDocument (root is <{root.Name.LocalName}>).");

        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var backupDatabase = false;
        ReadOptionPairs(root.Element("Options"), options, ref backupDatabase);
        ReadOptionPairs(root.Element("SynchronizationOptions"), options, ref backupDatabase);

        var (included, excluded) = ReadMappedObjects(root);
        return new CompFileDocument
        {
            Source = ReadEndpoint(root.Element("Source")),
            Target = ReadEndpoint(root.Element("Target")),
            Options = options,
            IncludedObjectNames = included,
            ExcludedObjectNames = excluded,
        };
    }

    private static Endpoint? ReadEndpoint(System.Xml.Linq.XElement? el)
    {
        if (el is null) return null;
        var cs = ReadConnectionString(el);
        if (string.IsNullOrWhiteSpace(cs)) return null;
        return new Endpoint(EndpointKind.ConnectionString, ConnectionString: cs);
    }

    internal static string? ReadConnectionString(System.Xml.Linq.XElement endpoint)
    {
        var direct = endpoint.Element("ConnectionString")?.Value;
        if (!string.IsNullOrWhiteSpace(direct))
            return direct.Trim();

        foreach (var p in endpoint.Descendants("PropertyValue"))
        {
            var name = (string?)p.Attribute("Name");
            if (name is not null &&
                name.Equals("ConnectionString", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(p.Value))
                return p.Value.Trim();
        }

        return null;
    }

    private static (IReadOnlyList<string> Included, IReadOnlyList<string> Excluded)
        ReadMappedObjects(System.Xml.Linq.XElement root)
    {
        var included = new List<string>();
        var excluded = new List<string>();
        var compared = root.Element("Mapping")?.Element("ComparedObjects");
        if (compared is null) return (included, excluded);

        foreach (var obj in compared.Elements("Object"))
        {
            var name = (string?)obj.Attribute("SourceName");
            if (string.IsNullOrWhiteSpace(name)) continue;
            var flag = (string?)obj.Attribute("Included") ?? "True";
            if (IsXmlTrue(flag))
                included.Add(name.Trim());
            else
                excluded.Add(name.Trim());
        }

        return (included, excluded);
    }

    private static void ReadOptionPairs(
        System.Xml.Linq.XElement? section,
        Dictionary<string, string> options,
        ref bool backupDatabase)
    {
        if (section is null) return;

        var raw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in section.Elements("PropertyValue"))
        {
            var name = (string?)p.Attribute("Name");
            if (string.IsNullOrWhiteSpace(name)) continue;
            raw[name] = p.Value ?? "";
        }

        if (raw.TryGetValue("BackupDatabase", out var bd) && IsXmlTrue(bd))
            backupDatabase = true;

        ApplyMaskPair(raw, options, "ExcludeObjectsByMask", "ExcludeObjectsMask");
        ApplyMaskPair(raw, options, "IncludeObjectsByMask", "IncludeObjectsMask");
        ApplyMaskPair(raw, options, "IgnoreColumnsByMask", "MappingColumnsMask");

        foreach (var (name, value) in raw)
        {
            if (IsPairOrChrome(name)) continue;

            if (!KnownOptions.TryGetCanonical(name, out var canonical))
                continue;

            if (KnownOptions.IsUnsupportedInV1(canonical))
            {
                if (!IsXmlTrue(value)) continue;
                if (IsBackupChrome(canonical) && !backupDatabase) continue;
                throw new CompFileException(10,
                    $"Option {canonical} from /compfile is not implemented in ArtSync v1.");
            }

            options[canonical] = ToCliBool(value);
        }
    }

    private static void ApplyMaskPair(
        Dictionary<string, string> raw,
        Dictionary<string, string> options,
        string flagName,
        string maskName)
    {
        if (!raw.TryGetValue(flagName, out var flag) || !IsXmlTrue(flag))
            return;
        if (!raw.TryGetValue(maskName, out var mask) || string.IsNullOrWhiteSpace(mask))
            return;
        if (!KnownOptions.TryGetCanonical(flagName, out var canonical))
            canonical = flagName;
        options[canonical] = mask.Trim();
    }

    private static bool IsPairOrChrome(string name)
        => name.Equals("ExcludeObjectsByMask", StringComparison.OrdinalIgnoreCase)
        || name.Equals("ExcludeObjectsMask", StringComparison.OrdinalIgnoreCase)
        || name.Equals("IncludeObjectsByMask", StringComparison.OrdinalIgnoreCase)
        || name.Equals("IncludeObjectsMask", StringComparison.OrdinalIgnoreCase)
        || name.Equals("IgnoreColumnsByMask", StringComparison.OrdinalIgnoreCase)
        || name.Equals("MappingColumnsMask", StringComparison.OrdinalIgnoreCase)
        || name.Equals("BackupDatabase", StringComparison.OrdinalIgnoreCase)
        || name.Equals("BackupType", StringComparison.OrdinalIgnoreCase)
        || name.Equals("BackupPathName", StringComparison.OrdinalIgnoreCase)
        || name.Equals("BackupExtensionName", StringComparison.OrdinalIgnoreCase)
        || name.Equals("CreateBackupFolder", StringComparison.OrdinalIgnoreCase)
        || name.Equals("NeedCompressBackup", StringComparison.OrdinalIgnoreCase)
        || name.Equals("AddBackupType", StringComparison.OrdinalIgnoreCase)
        || name.Equals("BackupExtension", StringComparison.OrdinalIgnoreCase)
        || name.Equals("BackupPath", StringComparison.OrdinalIgnoreCase);

    private static bool IsBackupChrome(string canonical)
        => canonical.Equals("CreateBackupFolder", StringComparison.OrdinalIgnoreCase)
        || canonical.Equals("NeedCompressBackup", StringComparison.OrdinalIgnoreCase)
        || canonical.Equals("AddBackupType", StringComparison.OrdinalIgnoreCase)
        || canonical.Equals("BackupExtension", StringComparison.OrdinalIgnoreCase)
        || canonical.Equals("BackupPath", StringComparison.OrdinalIgnoreCase);

    internal static bool IsXmlTrue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Equals("y", StringComparison.OrdinalIgnoreCase)
            || value.Equals("on", StringComparison.OrdinalIgnoreCase)
            || value.Equals("1", StringComparison.OrdinalIgnoreCase);
    }

    private static string ToCliBool(string value)
        => IsXmlTrue(value) ? "yes" : "no";
}

/// <summary>
/// Merges a <c>.dcomp</c> into a parsed <see cref="CommandRequest"/>.
/// Precedence matches SPEC §3.3: command line (already on the request) wins,
/// then argfile (already merged), then the project file.
/// </summary>
public static class CompFileMerger
{
    public static CommandRequest Apply(CommandRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CompFilePath))
            return request;

        var ext = Path.GetExtension(request.CompFilePath);
        if (ext.Equals(".scomp", StringComparison.OrdinalIgnoreCase) ||
            request.Operation == OperationType.SchemaCompare)
        {
            throw new CompFileException(10,
                "/compfile (.scomp) is not yet supported. " +
                "Provide /source and /target endpoints directly.");
        }

        if (!ext.Equals(".dcomp", StringComparison.OrdinalIgnoreCase) &&
            ext.Length > 0)
        {
            throw new CompFileException(10,
                "/compfile for /datacompare must be a .dcomp project file.");
        }

        var doc = CompFileParser.Load(request.CompFilePath);
        var options = new Dictionary<string, string>(request.Options, StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in doc.Options)
        {
            if (!options.ContainsKey(key))
                options[key] = value;
        }

        var exclude = UnionMasks(
            options.GetValueOrDefault("ExcludeObjectsByMask"),
            doc.ExcludedObjectNames);
        if (!string.IsNullOrWhiteSpace(exclude))
            options["ExcludeObjectsByMask"] = exclude;

        // Devart mapping checkboxes are an allowlist. Without this, ArtSync
        // compares every non-excluded table in the database.
        if (doc.IncludedObjectNames.Count > 0
            && !request.Options.ContainsKey("IncludeObjectsByMask"))
        {
            options["IncludeObjectsByMask"] = string.Join(",", doc.IncludedObjectNames);
        }

        var warnings = request.Warnings.ToList();
        var source = request.Source ?? doc.Source;
        var target = request.Target ?? doc.Target;
        if (request.Source is null && LooksPasswordless(doc.Source))
            warnings.Add("/compfile source has no Password=; pass /source connection:\"…\" to override.");
        if (request.Target is null && LooksPasswordless(doc.Target))
            warnings.Add("/compfile target has no Password=; pass /target connection:\"…\" to override.");

        return request with
        {
            Source = source,
            Target = target,
            Options = options,
            Warnings = warnings,
        };
    }

    private static string UnionMasks(string? existing, IReadOnlyList<string> extra)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(existing))
        {
            foreach (var p in existing.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                parts.Add(p);
        }

        foreach (var name in extra)
        {
            if (parts.Exists(p => p.Equals(name, StringComparison.OrdinalIgnoreCase)))
                continue;
            parts.Add(name);
        }

        return string.Join(",", parts);
    }

    private static bool LooksPasswordless(Endpoint? ep)
    {
        var cs = ep?.ConnectionString;
        if (string.IsNullOrWhiteSpace(cs)) return false;
        return cs.IndexOf("Password=", StringComparison.OrdinalIgnoreCase) < 0;
    }
}
