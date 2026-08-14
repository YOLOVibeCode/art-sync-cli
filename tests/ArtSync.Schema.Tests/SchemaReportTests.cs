using ArtSync.Abstractions;
using ArtSync.Compat;
using ArtSync.Reporting;
using ArtSync.Schema;
using FluentAssertions;

namespace ArtSync.Schema.Tests;

public sealed class SchemaReportTests
{
    [Fact]
    public void SchemaReport_Xml_InferredFromExtension()
    {
        var path = Path.Combine(Path.GetTempPath(), $"artsync-schema-{Guid.NewGuid():N}.xml");
        try
        {
            var reporter = new HtmlXmlCsvReporter(new SecretRedactor());
            var info = new SchemaCompareInfo(false, false, 1, ["[dbo].[AuditLog]"], []);
            var req = MakeRequest(path);

            reporter.WriteSchemaReport(path, "", info, req);

            var xml = File.ReadAllText(path);
            xml.Should().Contain("<SchemaCompareReport>");
            xml.Should().Contain("AuditLog");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SchemaHandler_WritesLogAndReport()
    {
        var report = Path.Combine(Path.GetTempPath(), $"artsync-schema-{Guid.NewGuid():N}.html");
        var log = Path.Combine(Path.GetTempPath(), $"artsync-schema-{Guid.NewGuid():N}.log");
        try
        {
            var fake = new FakeSchemaCompare(
                info: new SchemaCompareInfo(true, false, 0, [], []));
            var handler = new SchemaOperationHandler(
                fake,
                p => FileOperationLogger.Open(p),
                new HtmlXmlCsvReporter(new SecretRedactor()));

            var result = handler.Run(MakeRequest(report) with { LogPath = log, ReportPath = report });
            result.ExitCode.Should().Be(100);

            File.Exists(report).Should().BeTrue();
            File.ReadAllText(log).Should().Contain("EXIT 100");
        }
        finally
        {
            File.Delete(report);
            File.Delete(log);
        }
    }

    private static CommandRequest MakeRequest(string reportPath) => new(
        Operation: OperationType.SchemaCompare,
        Source: new Endpoint(EndpointKind.LiveSplit, "Src", "SrcDb"),
        Target: new Endpoint(EndpointKind.LiveSplit, "Tgt", "TgtDb"),
        SyncMode: SyncMode.None,
        SyncFilePath: null,
        ArgFilePath: null,
        CompFilePath: null,
        FilterFilePath: null,
        ReportPath: reportPath,
        LogPath: null,
        ReportFormat: null,
        Quiet: true,
        Argv0: "schemacompare",
        Options: new Dictionary<string, string>(),
        Warnings: Array.Empty<string>());
}
