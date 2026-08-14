using ArtSync.Abstractions;

namespace ArtSync.Data;

/// <summary>
/// Pure in-memory comparison of two hash-stream results.
/// Loads both sides into dictionaries keyed by PkKey, then classifies each row.
/// No SQL is executed here.
/// </summary>
internal sealed class SqlDiffClassifier
{
    public IReadOnlyList<RowDiff> Classify(
        string tableName,
        IEnumerable<(string PkKey, byte[] RowHash, IReadOnlyList<(string Col, string Val)> PkValues)> source,
        IEnumerable<(string PkKey, byte[] RowHash, IReadOnlyList<(string Col, string Val)> PkValues)> target)
    {
        // Load into dictionaries — O(n) memory, acceptable for v1.
        var srcDict = new Dictionary<string, (byte[] Hash, IReadOnlyList<(string, string)> Pk)>(
            StringComparer.Ordinal);
        foreach (var row in source)
            srcDict[row.PkKey] = (row.RowHash, row.PkValues);

        var tgtDict = new Dictionary<string, (byte[] Hash, IReadOnlyList<(string, string)> Pk)>(
            StringComparer.Ordinal);
        foreach (var row in target)
            tgtDict[row.PkKey] = (row.RowHash, row.PkValues);

        var diffs = new List<RowDiff>();

        // Rows in source
        foreach (var (pkKey, (srcHash, pkVals)) in srcDict)
        {
            if (!tgtDict.TryGetValue(pkKey, out var tgtEntry))
            {
                diffs.Add(new RowDiff(tableName, RowDiffKind.OnlyInSource, ToPkList(pkVals)));
            }
            else if (!srcHash.SequenceEqual(tgtEntry.Hash))
            {
                diffs.Add(new RowDiff(tableName, RowDiffKind.Different, ToPkList(pkVals)));
            }
            // else: Identical — no diff entry
        }

        // Rows only in target
        foreach (var (pkKey, (_, pkVals)) in tgtDict)
        {
            if (!srcDict.ContainsKey(pkKey))
                diffs.Add(new RowDiff(tableName, RowDiffKind.OnlyInTarget, ToPkList(pkVals)));
        }

        return diffs;
    }

    private static IReadOnlyList<(string Column, object? Value)> ToPkList(
        IReadOnlyList<(string Col, string Val)> pkVals)
        => pkVals.Select(p => (p.Col, (object?)p.Val)).ToList();
}
