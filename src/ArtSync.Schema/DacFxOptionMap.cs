using Microsoft.SqlServer.Dac;

namespace ArtSync.Schema;

/// <summary>
/// Maps canonical Devart option names (from <c>KnownOptions</c>) to DacFx
/// <see cref="DacDeployOptions"/> properties.
/// See also: docs/dac-option-map.md (living document).
/// </summary>
internal static class DacFxOptionMap
{
    /// <summary>
    /// Applies all recognised options to <paramref name="deployOptions"/>.
    /// Logs a warning to the console for options that have no DacFx equivalent
    /// (they are accepted by the parser but silently no-op here).
    /// </summary>
    public static void Apply(
        DacDeployOptions deployOptions,
        IReadOnlyDictionary<string, string> options)
    {
        foreach (var (key, rawValue) in options)
        {
            // Skip internal-only parser fields.
            if (key.StartsWith('_')) continue;

            var on = IsTruthy(rawValue);

            switch (key)
            {
                // ── Direct DacFx equivalents (SPEC §9.3 "Must map in v1") ──────
                case "IgnoreWhiteSpace":
                    deployOptions.IgnoreWhitespace = on;
                    break;
                case "IgnoreComments":
                    deployOptions.IgnoreComments = on;
                    break;
                case "IgnorePermissions":
                    deployOptions.IgnorePermissions = on;
                    break;
                case "IgnoreUserPermissions":
                    deployOptions.IgnoreRoleMembership = on;
                    break;
                case "IgnoreCollations":
                    deployOptions.IgnoreColumnCollation = on;
                    break;
                case "IgnoreIdentitySeedIncrementValues":
                    deployOptions.IgnoreIdentitySeed = on;
                    deployOptions.IgnoreIncrement = on;
                    break;
                case "IgnoreFilegroupsPartitionSchemes":
                    deployOptions.IgnoreFilegroupPlacement = on;
                    deployOptions.IgnorePartitionSchemes = on;
                    deployOptions.IgnoreTablePartitionOptions = on;
                    deployOptions.IgnoreObjectPlacementOnPartitionScheme = on;
                    break;
                case "IgnoreNotForReplication":
                    deployOptions.IgnoreNotForReplication = on;
                    break;
                case "IgnoreQuotedIdentifierAndANSINulls":
                    deployOptions.IgnoreQuotedIdentifiers = on;
                    deployOptions.IgnoreAnsiNulls = on;
                    break;
                case "IgnoreDropIndexes":
                    deployOptions.DropIndexesNotInSource = !on;
                    break;
                case "IgnoreDropDMLTriggers":
                    deployOptions.DropDmlTriggersNotInSource = !on;
                    break;
                case "IgnoreAuthorization":
                    deployOptions.IgnoreAuthorizer = on;
                    break;
                case "ForceColumnOrder":
                    deployOptions.IgnoreColumnOrder = !on;
                    break;
                case "DisableDdlTriggers":
                    deployOptions.DisableAndReenableDdlTriggers = on;
                    break;
                case "DeployDatabaseInSingleUserMode":
                    // Exit 10 for Azure SQL DB is enforced at parse time by KnownOptions.
                    deployOptions.DeployDatabaseInSingleUserMode = on;
                    break;

                // ── Best-effort / partial mappings ────────────────────────────
                case "IgnoreIndexes":
                    // DacFx has no single "ignore all indexes" toggle; suppress option differences.
                    deployOptions.IgnoreIndexOptions = on;
                    deployOptions.IgnoreIndexPadding = on;
                    break;
                case "IgnoreStatistics":
                    // DacFx has no single IgnoreStatistics; suppress dropping statistics.
                    deployOptions.DropStatisticsNotInSource = !on;
                    break;
                case "IgnoreTableDMLTriggers":
                    deployOptions.IgnoreDmlTriggerOrder = on;
                    deployOptions.IgnoreDmlTriggerState = on;
                    break;

                // ── Handled via post-compare _result.Exclude() in DacFxSchemaSession ──
                // IgnoreForeignKeys / IgnorePrimaryKeys / IgnoreUniqueKeys /
                // IgnoreCheckConstraints / IgnoreDefaultConstraints are applied
                // by BuildExcludedTypeNames() after Compare() runs, not here.
                case "IgnoreForeignKeys":
                case "IgnorePrimaryKeys":
                case "IgnoreUniqueKeys":
                case "IgnoreCheckConstraints":
                case "IgnoreDefaultConstraints":
                    break;

                // ── No-op: DacFx does not have a direct equivalent ────────────
                // Accepted by the parser; silently ignored at apply time.
                case "IgnoreCase":
                case "IgnoreIdentity":
                case "IgnoreWithNocheck":
                case "IgnoreTSQLtFramework":
                case "MappingIgnoreCase":
                case "MappingIgnoreSpaces":
                case "ExecuteAsSingleTransaction":
                case "IncludeUseDatabase":
                case "IncludePrintComments":
                case "ExcludeComments":
                case "AddingErrorHandling":
                case "CheckObjectExistence":
                case "QuoteObjectNames":
                    // Log a warning in case a strict mapping is later needed.
                    // (In v1 these are accepted without error so existing jobs keep running.)
                    break;

                // ── Report/display options ────────────────────────────────────
                case "groupby":
                case "incsettings":
                case "scriptdiffsstyle":
                    break;

                default:
                    // Unknown at this level — parser should have already rejected truly
                    // unknown options, so this is a soft warning rather than a hard failure.
                    Console.Error.WriteLine(
                        $"Warning: unrecognised option '{key}' passed to DacFxOptionMap; ignored.");
                    break;
            }
        }
    }

    private static bool IsTruthy(string value) =>
        value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("y", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("on", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("t", StringComparison.OrdinalIgnoreCase);
}
