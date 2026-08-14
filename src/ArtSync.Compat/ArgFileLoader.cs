using ArtSync.Abstractions;

namespace ArtSync.Compat;

/// <summary>
/// Loads a Devart argfile (one logical switch per line, double-quoted values
/// handled as the OS would) and returns a pre-split argv-style token list.
/// </summary>
public sealed class ArgFileLoader : IArgFileLoader
{
    public IReadOnlyList<string> Load(string path)
    {
        // Let FileNotFoundException propagate for the caller to map to exit 10.
        var lines = File.ReadAllLines(path);
        var tokens = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;

            tokens.AddRange(TokenizeLine(trimmed));
        }

        return tokens.AsReadOnly();
    }

    /// <summary>
    /// Splits a single argfile line into argv-style tokens.
    /// Quoted regions are merged into a single token (quotes stripped),
    /// matching how a Windows command shell would hand the token to the process.
    /// </summary>
    internal static IEnumerable<string> TokenizeLine(string line)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuote = false;

        for (var k = 0; k < line.Length; k++)
        {
            var c = line[k];

            if (c == '"')
            {
                // Toggle quoted region; the quote character itself is stripped.
                inQuote = !inQuote;
            }
            else if (char.IsWhiteSpace(c) && !inQuote)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
            tokens.Add(current.ToString());

        return tokens;
    }
}
