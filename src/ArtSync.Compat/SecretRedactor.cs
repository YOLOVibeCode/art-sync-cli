using System.Text.RegularExpressions;
using ArtSync.Abstractions;

namespace ArtSync.Compat;

/// <summary>
/// Replaces password/credential values before they reach logs or reports
/// (SPEC §13 SEC-1).  The original strings are never mutated.
/// </summary>
public sealed partial class SecretRedactor : ISecretRedactor
{
    private const string Replacement = "***";

    // ── Connection-string patterns (key=value;…) ──────────────────────────────

    [GeneratedRegex(
        @"(?i)(password|pwd)\s*=\s*([^;""]*)",
        RegexOptions.Compiled)]
    private static partial Regex ConnStrPasswordRegex();

    // ── Split-param patterns (password:value) ─────────────────────────────────

    [GeneratedRegex(
        @"(?i)^(password):(.+)$",
        RegexOptions.Compiled)]
    private static partial Regex SplitParamPasswordRegex();

    // ── /password:value switch ────────────────────────────────────────────────

    [GeneratedRegex(
        @"(?i)^(/password:)(.+)$",
        RegexOptions.Compiled)]
    private static partial Regex SwitchPasswordRegex();

    // ─────────────────────────────────────────────────────────────────────────

    public string Redact(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        // Mask connection-string passwords.
        input = ConnStrPasswordRegex().Replace(input, m =>
            m.Groups[1].Value + "=" + Replacement);

        return input;
    }

    public IReadOnlyList<string> RedactArgv(IReadOnlyList<string> argv)
    {
        var result = new string[argv.Count];
        for (var k = 0; k < argv.Count; k++)
        {
            var tok = argv[k];

            // /password:secret  →  /password:***
            var m1 = SwitchPasswordRegex().Match(tok);
            if (m1.Success)
            {
                result[k] = m1.Groups[1].Value + Replacement;
                continue;
            }

            // password:secret  (endpoint sub-param)  →  password:***
            var m2 = SplitParamPasswordRegex().Match(tok);
            if (m2.Success)
            {
                result[k] = m2.Groups[1].Value + ":" + Replacement;
                continue;
            }

            // connection:"…;Password=secret;…"  →  redact inline
            result[k] = Redact(tok);
        }

        return result;
    }
}
