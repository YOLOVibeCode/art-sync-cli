using Microsoft.Data.SqlClient;

namespace ArtSync.Data;

/// <summary>
/// Reads foreign-key edges and sorts tables so inserts go parents-first and
/// deletes go children-first (SPEC §10.2 step 8). Cycles fall back to the
/// original order; <c>DisableForeignKeys</c> covers those during apply.
/// </summary>
internal static class SqlFkGraph
{
    public static IReadOnlyList<(string Child, string Parent)> Read(string connectionString)
    {
        const string sql = """
            SELECT
                QUOTENAME(OBJECT_SCHEMA_NAME(fk.parent_object_id))
                    + N'.' + QUOTENAME(OBJECT_NAME(fk.parent_object_id))     AS ChildTable,
                QUOTENAME(OBJECT_SCHEMA_NAME(fk.referenced_object_id))
                    + N'.' + QUOTENAME(OBJECT_NAME(fk.referenced_object_id)) AS ParentTable
            FROM sys.foreign_keys fk
            """;

        var edges = new List<(string Child, string Parent)>();
        using var conn = new SqlConnection(connectionString);
        conn.Open();
        using var cmd = new SqlCommand(sql, conn);
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
            edges.Add(((string)rdr["ChildTable"], (string)rdr["ParentTable"]));
        return edges;
    }

    /// <summary>Parents before children. Self-FKs are ignored as edges.</summary>
    public static IReadOnlyList<string> ParentsFirst(
        IEnumerable<string> tables,
        IReadOnlyList<(string Child, string Parent)> edges)
    {
        var original = tables
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var remaining = new HashSet<string>(original, StringComparer.OrdinalIgnoreCase);

        var relevant = edges
            .Where(e => remaining.Contains(e.Child)
                     && remaining.Contains(e.Parent)
                     && !e.Child.Equals(e.Parent, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var result = new List<string>(original.Count);

        while (remaining.Count > 0)
        {
            var roots = original
                .Where(t => remaining.Contains(t)
                         && !relevant.Any(e =>
                                e.Child.Equals(t, StringComparison.OrdinalIgnoreCase)
                                && remaining.Contains(e.Parent)))
                .ToList();

            if (roots.Count == 0)
            {
                result.AddRange(original.Where(remaining.Contains));
                break;
            }

            foreach (var r in roots)
            {
                result.Add(r);
                remaining.Remove(r);
            }
        }

        return result;
    }

    public static IReadOnlyList<string> ChildrenFirst(
        IEnumerable<string> tables,
        IReadOnlyList<(string Child, string Parent)> edges)
        => ParentsFirst(tables, edges).Reverse().ToList();

    /// <summary>
    /// Expands <paramref name="tables"/> with every FK neighbour so NOCHECK
    /// covers constraints that fire when a parent is deleted.
    /// </summary>
    public static IReadOnlyList<string> InvolvedTables(
        IEnumerable<string> tables,
        IReadOnlyList<(string Child, string Parent)> edges)
    {
        var involved = new HashSet<string>(tables, StringComparer.OrdinalIgnoreCase);
        bool changed;
        do
        {
            changed = false;
            foreach (var (child, parent) in edges)
            {
                if (involved.Contains(child) && involved.Add(parent)) changed = true;
                if (involved.Contains(parent) && involved.Add(child)) changed = true;
            }
        } while (changed);

        return involved.ToList();
    }
}
