using ArtSync.Abstractions;
using System.Text;
using System.Web;

namespace ArtSync.Reporting;

/// <summary>
/// Writes comparison reports in HTML, XML (schema), or CSV (data) format.
/// Format is chosen by the <paramref name="format"/> argument; if the value is
/// empty or unrecognised the path extension is used to infer the format
/// (html → HTML, xml → XML for schema, csv → CSV for data).
/// IO failures throw <see cref="ReportIoException"/> (exit 107).
/// </summary>
public sealed class HtmlXmlCsvReporter : IResultReporter
{
    private readonly ISecretRedactor _redactor;

    public HtmlXmlCsvReporter(ISecretRedactor redactor) => _redactor = redactor;

    // ── Schema ────────────────────────────────────────────────────────────────

    public void WriteSchemaReport(
        string path,
        string format,
        SchemaCompareInfo info,
        CommandRequest request)
    {
        var fmt = ResolveFormat(path, format, preferred: "html", csvAlternative: false);
        try
        {
            EnsureDir(path);
            var content = fmt == "xml"
                ? BuildSchemaXml(info, request)
                : BuildSchemaHtml(info, request);
            File.WriteAllText(path, content, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is not ReportIoException)
        {
            throw new ReportIoException($"Cannot write schema report to '{path}': {ex.Message}", ex);
        }
    }

    // ── Data ──────────────────────────────────────────────────────────────────

    public void WriteDataReport(
        string path,
        string format,
        DataCompareInfo info,
        IReadOnlyList<RowDiff> diffs,
        CommandRequest request)
    {
        var fmt = ResolveFormat(path, format, preferred: "html", csvAlternative: true);
        try
        {
            EnsureDir(path);
            var content = fmt == "csv"
                ? BuildDataCsv(info, diffs, request)
                : BuildDataHtml(info, diffs, request);
            File.WriteAllText(path, content, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is not ReportIoException)
        {
            throw new ReportIoException($"Cannot write data report to '{path}': {ex.Message}", ex);
        }
    }

    // ── Schema builders ───────────────────────────────────────────────────────

    private string BuildSchemaHtml(SchemaCompareInfo info, CommandRequest request)
    {
        var sb = new StringBuilder();
        AppendHtmlHeader(sb, "ArtSync Schema Compare Report");

        sb.AppendLine("<h1>ArtSync Schema Compare Report</h1>");
        AppendSummaryTable(sb, request,
            ("Result", info.IsIdentical ? "Identical" : info.HasNoComparableObjects ? "No comparable objects" : "Differences found"),
            ("Difference count", info.DifferenceCount.ToString()));

        if (info.DifferentObjectNames.Count > 0)
        {
            sb.AppendLine("<h2>Changed objects</h2>");
            sb.AppendLine("<table>");
            sb.AppendLine("<thead><tr><th>#</th><th>Object name</th></tr></thead><tbody>");
            int i = 1;
            foreach (var name in info.DifferentObjectNames)
                sb.AppendLine($"<tr><td>{i++}</td><td>{H(name)}</td></tr>");
            sb.AppendLine("</tbody></table>");
        }

        if (info.Messages.Count > 0)
        {
            sb.AppendLine("<h2>Messages</h2><ul>");
            foreach (var m in info.Messages)
                sb.AppendLine($"<li>{H(m)}</li>");
            sb.AppendLine("</ul>");
        }

        AppendHtmlFooter(sb);
        return sb.ToString();
    }

    private string BuildSchemaXml(SchemaCompareInfo info, CommandRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine("""<?xml version="1.0" encoding="UTF-8"?>""");
        sb.AppendLine("<SchemaCompareReport>");
        sb.AppendLine($"  <GeneratedUtc>{DateTime.UtcNow:O}</GeneratedUtc>");
        sb.AppendLine($"  <Source>{X(_redactor.Redact(request.Source?.Server ?? ""))}</Source>");
        sb.AppendLine($"  <Target>{X(_redactor.Redact(request.Target?.Server ?? ""))}</Target>");
        sb.AppendLine($"  <IsIdentical>{info.IsIdentical}</IsIdentical>");
        sb.AppendLine($"  <DifferenceCount>{info.DifferenceCount}</DifferenceCount>");
        if (info.DifferentObjectNames.Count > 0)
        {
            sb.AppendLine("  <Differences>");
            foreach (var name in info.DifferentObjectNames)
                sb.AppendLine($"    <Object>{X(name)}</Object>");
            sb.AppendLine("  </Differences>");
        }
        sb.AppendLine("</SchemaCompareReport>");
        return sb.ToString();
    }

    // ── Data builders ─────────────────────────────────────────────────────────

    private string BuildDataHtml(DataCompareInfo info, IReadOnlyList<RowDiff> diffs, CommandRequest request)
    {
        var sb = new StringBuilder();
        AppendHtmlHeader(sb, "ArtSync Data Compare Report");

        sb.AppendLine("<h1>ArtSync Data Compare Report</h1>");
        AppendSummaryTable(sb, request,
            ("Comparable tables", info.ComparableTables.Count.ToString()),
            ("Skipped tables", info.SkippedTables.Count.ToString()),
            ("Source-only rows", info.OnlyInSourceRows.ToString()),
            ("Target-only rows", info.OnlyInTargetRows.ToString()),
            ("Changed rows", info.DifferentRows.ToString()),
            ("Total differences", info.TotalDifferentRows.ToString()));

        if (diffs.Count > 0)
        {
            sb.AppendLine("<h2>Row differences</h2>");
            sb.AppendLine("<table>");
            sb.AppendLine("<thead><tr><th>Table</th><th>Kind</th><th>PK values</th></tr></thead><tbody>");
            foreach (var d in diffs)
            {
                var pk = string.Join(", ", d.PkValues.Select(p => $"{H(p.Column)}={H(p.Value?.ToString() ?? "NULL")}"));
                sb.AppendLine($"<tr><td>{H(d.TableName)}</td><td>{H(d.Kind.ToString())}</td><td>{pk}</td></tr>");
            }
            sb.AppendLine("</tbody></table>");
        }

        if (info.SkippedTables.Count > 0)
        {
            sb.AppendLine("<h2>Skipped tables</h2><ul>");
            foreach (var t in info.SkippedTables)
                sb.AppendLine($"<li>{H(t)}</li>");
            sb.AppendLine("</ul>");
        }

        AppendHtmlFooter(sb);
        return sb.ToString();
    }

    private static string BuildDataCsv(DataCompareInfo info, IReadOnlyList<RowDiff> diffs, CommandRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Table,Kind,PkValues");
        foreach (var d in diffs)
        {
            var pk = string.Join("; ", d.PkValues.Select(p => $"{p.Column}={p.Value ?? "NULL"}"));
            sb.AppendLine($"{CsvQ(d.TableName)},{CsvQ(d.Kind.ToString())},{CsvQ(pk)}");
        }
        return sb.ToString();
    }

    // ── HTML helpers ──────────────────────────────────────────────────────────

    private static void AppendHtmlHeader(StringBuilder sb, string title)
    {
        sb.Append("<!DOCTYPE html>\n<html lang=\"en\"><head>\n<meta charset=\"UTF-8\">\n");
        sb.Append($"<title>{H(title)}</title>\n");
        sb.Append("<style>\n");
        sb.Append("  body  { font-family: sans-serif; margin: 2em; }\n");
        sb.Append("  table { border-collapse: collapse; width: 100%; }\n");
        sb.Append("  th,td { border: 1px solid #ccc; padding: 6px 10px; text-align: left; }\n");
        sb.Append("  th    { background: #f0f0f0; }\n");
        sb.Append("  tr:nth-child(even) { background: #fafafa; }\n");
        sb.Append("  h1    { color: #333; }\n");
        sb.Append("  h2    { color: #555; margin-top: 2em; }\n");
        sb.Append("</style>\n</head><body>\n");
        sb.AppendLine($"<p><small>Generated: {DateTime.UtcNow:O}</small></p>");
    }

    private void AppendSummaryTable(StringBuilder sb, CommandRequest request, params (string Label, string Value)[] rows)
    {
        sb.AppendLine("<h2>Summary</h2>");
        sb.AppendLine("<table><thead><tr><th>Property</th><th>Value</th></tr></thead><tbody>");
        sb.AppendLine($"<tr><td>Source</td><td>{H(_redactor.Redact(EndpointDisplay(request.Source)))}</td></tr>");
        sb.AppendLine($"<tr><td>Target</td><td>{H(_redactor.Redact(EndpointDisplay(request.Target)))}</td></tr>");
        foreach (var (label, value) in rows)
            sb.AppendLine($"<tr><td>{H(label)}</td><td>{H(value)}</td></tr>");
        sb.AppendLine("</tbody></table>");
    }

    private static void AppendHtmlFooter(StringBuilder sb)
        => sb.AppendLine("</body></html>");

    // ── Utilities ─────────────────────────────────────────────────────────────

    private static string ResolveFormat(string path, string format, string preferred, bool csvAlternative)
    {
        if (!string.IsNullOrWhiteSpace(format))
        {
            var f = format.Trim().ToLowerInvariant();
            return f is "html" or "xml" or "csv" ? f : preferred;
        }

        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        if (ext == "xml") return "xml";
        if (csvAlternative && ext == "csv") return "csv";
        return preferred;
    }

    private static void EnsureDir(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    }

    private static string EndpointDisplay(Endpoint? ep)
    {
        if (ep is null) return "";
        if (ep.Kind == EndpointKind.ConnectionString) return ep.ConnectionString ?? "";
        return $"{ep.Server}/{ep.Database}";
    }

    /// <summary>HTML-encodes a string for safe output.</summary>
    private static string H(string s) => HttpUtility.HtmlEncode(s);

    /// <summary>XML-encodes a string for safe output.</summary>
    private static string X(string s) => System.Security.SecurityElement.Escape(s) ?? "";

    /// <summary>CSV-quotes a field value.</summary>
    private static string CsvQ(string s)
        => s.Contains(',') || s.Contains('"') || s.Contains('\n')
            ? $"\"{s.Replace("\"", "\"\"")}\""
            : s;
}
