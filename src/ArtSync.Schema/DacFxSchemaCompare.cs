using ArtSync.Abstractions;
using Microsoft.SqlServer.Dac;
using Microsoft.SqlServer.Dac.Compare;

namespace ArtSync.Schema;

/// <summary>
/// ISchemaCompare implementation backed by Microsoft.SqlServer.Dac.Compare.
/// All DacFx types are confined to this file and DacFxOptionMap.
/// </summary>
public sealed class DacFxSchemaCompare : ISchemaCompare
{
    public ISchemaSession OpenSession(
        Endpoint source,
        Endpoint target,
        IReadOnlyDictionary<string, string> options)
    {
        // Load filter file before making any network connection so we can
        // propagate exit-114 errors without waiting for SQL.
        ObjectFilterSet? filter = null;
        if (options.TryGetValue("_FilterFilePath", out var filterPath)
            && !string.IsNullOrEmpty(filterPath))
        {
            filter = new ScfltLoader().Load(filterPath);   // throws SchemaFilterException on error
        }

        try
        {
            var srcCs = BuildConnectionString(source, "source");
            var tgtCs = BuildConnectionString(target, "target");
            var srcEp = new SchemaCompareDatabaseEndpoint(srcCs);
            var tgtEp = new SchemaCompareDatabaseEndpoint(tgtCs);
            var comparison = new SchemaComparison(srcEp, tgtEp);

            DacFxOptionMap.Apply(comparison.Options, options);

            return new DacFxSchemaSession(comparison, filter, options);
        }
        catch (SchemaFilterException) { throw; }
        catch (Exception ex) when (IsConnectionException(ex))
        {
            throw new SchemaConnectionException(ex.Message, ex);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    internal static string BuildConnectionString(Endpoint ep, string role)
    {
        if (ep.Kind == EndpointKind.ConnectionString)
        {
            if (string.IsNullOrWhiteSpace(ep.ConnectionString))
                throw new ArgumentException($"Connection string for {role} endpoint is empty.");
            return ep.ConnectionString!;
        }

        // LiveSplit → build an ADO.NET connection string.
        if (string.IsNullOrWhiteSpace(ep.Server))
            throw new ArgumentException($"Missing server for {role} endpoint.");
        if (string.IsNullOrWhiteSpace(ep.Database))
            throw new ArgumentException($"Missing database for {role} endpoint.");

        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder
        {
            DataSource             = ep.Server,
            InitialCatalog         = ep.Database,
            Encrypt                = true,
            TrustServerCertificate = true,
        };

        if (!string.IsNullOrEmpty(ep.User))
        {
            builder.UserID = ep.User;
            builder.Password = ep.Password ?? string.Empty;
            builder.IntegratedSecurity = false;
        }
        else
        {
            builder.IntegratedSecurity = true;
        }

        return builder.ConnectionString;
    }

    private static bool IsConnectionException(Exception ex)
    {
        var msg = ex.Message;
        return msg.Contains("connection", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("login", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("server", StringComparison.OrdinalIgnoreCase)
            || ex is Microsoft.Data.SqlClient.SqlException;
    }
}

/// <summary>
/// Wraps a DacFx SchemaComparison and caches the result between Compare/Script/Publish.
/// </summary>
internal sealed class DacFxSchemaSession : ISchemaSession
{
    private readonly SchemaComparison _comparison;
    private readonly ObjectFilterSet? _filter;
    private readonly IReadOnlyDictionary<string, string> _options;
    private SchemaComparisonResult? _result;

    public DacFxSchemaSession(
        SchemaComparison comparison,
        ObjectFilterSet? filter = null,
        IReadOnlyDictionary<string, string>? options = null)
    {
        _comparison = comparison;
        _filter = filter;
        _options = options ?? new Dictionary<string, string>();
    }

    public SchemaCompareInfo Compare()
    {
        try
        {
            _result = _comparison.Compare();

            if (!_result.IsValid)
            {
                var errors = _result.GetErrors()
                    .Select(e => e.Message)
                    .ToList();

                // DacFx reports connection failures as an invalid result rather than
                // throwing. Detect them and re-map to SchemaConnectionException → exit 40.
                var connMsg = errors.FirstOrDefault(IsConnectionErrorMessage);
                if (connMsg is not null)
                    throw new SchemaConnectionException(connMsg);

                return new SchemaCompareInfo(
                    IsIdentical: false,
                    HasNoComparableObjects: true,
                    DifferenceCount: 0,
                    DifferentObjectNames: [],
                    Messages: errors);
            }

            // Build set of model type names to exclude based on IgnoreXxx options.
            var excludedTypes = BuildExcludedTypeNames(_options);

            // Apply object filter and option-based type exclusions.
            foreach (var diff in _result.Differences)
            {
                if (!diff.IsExcludable) continue;
                var typeName = diff.SourceObject?.ObjectType.Name
                            ?? diff.TargetObject?.ObjectType.Name
                            ?? string.Empty;
                var name = diff.Name ?? string.Empty;

                if (excludedTypes.Contains(typeName))
                {
                    _result.Exclude(diff);
                    continue;
                }

                if (_filter is not null && !_filter.IsIncluded(typeName, name))
                    _result.Exclude(diff);
            }

            // Count only differences that are still included after filtering.
            var includedDiffs = _result.Differences
                .Where(d => d.Included)
                .ToList();

            if (includedDiffs.Count == 0)
                return new SchemaCompareInfo(true, false, 0, [], []);

            var names = includedDiffs
                .Select(d => d.Name ?? "<unnamed>")
                .ToList<string>();

            return new SchemaCompareInfo(
                IsIdentical: false,
                HasNoComparableObjects: false,
                DifferenceCount: includedDiffs.Count,
                DifferentObjectNames: names,
                Messages: []);
        }
        catch (Exception ex)
        {
            throw new SchemaConnectionException(ex.Message, ex);
        }
    }

    public string? GenerateScript()
    {
        EnsureCompared();
        try
        {
            // GenerateScript takes the target database name for context.
            var targetName = (_comparison.Target as SchemaCompareDatabaseEndpoint)?.DatabaseName
                             ?? "Target";
            var scriptResult = _result!.GenerateScript(targetName);
            return scriptResult.Success ? scriptResult.Script : null;
        }
        catch (Exception ex)
        {
            throw new SchemaIoException($"Script generation failed: {ex.Message}", ex);
        }
    }

    public void Publish()
    {
        EnsureCompared();
        try
        {
            var publishResult = _result!.PublishChangesToDatabase();
            if (!publishResult.Success)
            {
                var msgs = publishResult.Errors
                    .Select(e => e.Message)
                    .Take(5);
                throw new SchemaConnectionException(
                    $"Publish failed: {string.Join("; ", msgs)}");
            }
        }
        catch (SchemaConnectionException) { throw; }
        catch (Exception ex)
        {
            throw new SchemaConnectionException($"Publish failed: {ex.Message}", ex);
        }
    }

    public void Dispose() { }

    /// <summary>
    /// Maps Devart IgnoreXxx options to DacFx model type names so we can call
    /// _result.Exclude() for diffs of those types (ExcludeObjectTypes only covers
    /// top-level objects, not sub-object constraints).
    /// </summary>
    private static HashSet<string> BuildExcludedTypeNames(IReadOnlyDictionary<string, string> opts)
    {
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (IsOn(opts, "IgnoreForeignKeys"))       excluded.Add("ForeignKeyConstraint");
        if (IsOn(opts, "IgnorePrimaryKeys"))        excluded.Add("PrimaryKeyConstraint");
        if (IsOn(opts, "IgnoreUniqueKeys"))         excluded.Add("UniqueConstraint");
        if (IsOn(opts, "IgnoreCheckConstraints"))   excluded.Add("CheckConstraint");
        if (IsOn(opts, "IgnoreDefaultConstraints")) excluded.Add("DefaultConstraint");
        return excluded;
    }

    private static bool IsOn(IReadOnlyDictionary<string, string> opts, string key)
    {
        if (!opts.TryGetValue(key, out var v)) return false;
        return v.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || v.Equals("y",   StringComparison.OrdinalIgnoreCase)
            || v.Equals("on",  StringComparison.OrdinalIgnoreCase)
            || v.Equals("true",StringComparison.OrdinalIgnoreCase)
            || v.Equals("t",   StringComparison.OrdinalIgnoreCase);
    }

    private void EnsureCompared()
    {
        if (_result is null)
            throw new InvalidOperationException("Must call Compare() before GenerateScript() or Publish().");
    }

    /// <summary>
    /// Returns true when a DacFx error message looks like a network / login failure
    /// that should be surfaced as exit code 40 rather than 108.
    /// </summary>
    private static bool IsConnectionErrorMessage(string msg)
    {
        static bool Ci(string s, string sub) =>
            s.Contains(sub, StringComparison.OrdinalIgnoreCase);

        return Ci(msg, "network-related")
            || Ci(msg, "instance-specific")
            || Ci(msg, "server was not found")
            || Ci(msg, "could not open a connection")
            || Ci(msg, "login failed")
            || Ci(msg, "cannot open database")
            || Ci(msg, "connection attempt failed")
            || Ci(msg, "could not connect")
            // On macOS/Linux DacFx surfaces MSAL assembly load failure
            // when any connection attempt fails — treat as connection error.
            || Ci(msg, "Could not load file or assembly")
            || Ci(msg, "The system cannot find");
    }
}
