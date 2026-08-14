using ArtSync.Abstractions;
using ArtSync.Compat;
using ArtSync.Data;
using ArtSync.Reporting;
using FluentAssertions;

namespace ArtSync.Data.Tests;

public sealed class ReportingTests
{
    [Fact]
    public void Log_AppendsTimestampedLine_AndRedactsPassword()
    {
        var path = Path.Combine(Path.GetTempPath(), $"artsync-log-{Guid.NewGuid():N}.txt");
        try
        {
            using (var log = FileOperationLogger.Open(path, new SecretRedactor()))
            {
                log.LogLine("Password=SuperSecret;User=sa");
            }

            var text = File.ReadAllText(path);
            text.Should().Contain("Password=***");
            text.Should().NotContain("SuperSecret");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Log_UnwritablePath_ThrowsLogIoException()
    {
        var act = () => FileOperationLogger.Open("/this/path/does/not/exist/and/cannot/be/created/\0bad.log");
        act.Should().Throw<LogIoException>();
    }

    [Fact]
    public void DataReport_Html_IncludesRowDiffs()
    {
        var path = Path.Combine(Path.GetTempPath(), $"artsync-report-{Guid.NewGuid():N}.html");
        try
        {
            var reporter = new HtmlXmlCsvReporter(new SecretRedactor());
            var info = new DataCompareInfo(
                DataCompareStatus.HasDifferences, 1, 1, 0, 0,
                ["[dbo].[Customers]"], []);
            var diffs = new[]
            {
                new RowDiff("[dbo].[Customers]", RowDiffKind.OnlyInSource, [("CustomerId", (object?)9)]),
            };
            var req = MakeRequest(path);

            reporter.WriteDataReport(path, "html", info, diffs, req);

            var html = File.ReadAllText(path);
            html.Should().Contain("Customers");
            html.Should().Contain("OnlyInSource");
            html.Should().Contain("CustomerId");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void DataReport_Csv_InferredFromExtension()
    {
        var path = Path.Combine(Path.GetTempPath(), $"artsync-report-{Guid.NewGuid():N}.csv");
        try
        {
            var reporter = new HtmlXmlCsvReporter(new SecretRedactor());
            var info = new DataCompareInfo(
                DataCompareStatus.HasDifferences, 1, 0, 0, 1,
                ["[dbo].[T]"], []);
            var diffs = new[]
            {
                new RowDiff("[dbo].[T]", RowDiffKind.Different, [("Id", (object?)1)]),
            };

            reporter.WriteDataReport(path, "", info, diffs, MakeRequest(path));

            File.ReadAllText(path).Should().Contain("Table,Kind,PkValues");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void DataHandler_Report_UsesLastDiffs()
    {
        var path = Path.Combine(Path.GetTempPath(), $"artsync-handler-{Guid.NewGuid():N}.html");
        try
        {
            var diffs = new[] { new RowDiff("[dbo].[Orders]", RowDiffKind.OnlyInSource, [("Id", (object?)1)]) };
            var fake = new FakeDataCompare(
                new DataCompareInfo(DataCompareStatus.HasDifferences, 1, 1, 0, 0, ["[dbo].[Orders]"], []),
                diffs: diffs);

            var handler = new DataOperationHandler(
                fake,
                reporter: new HtmlXmlCsvReporter(new SecretRedactor()));

            var req = MakeRequest(path) with { ReportPath = path, ReportFormat = "HTML" };
            handler.Run(req).ExitCode.Should().Be(101);

            File.ReadAllText(path).Should().Contain("Orders");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void MapKey_StripsSpacesAndUnderscores()
    {
        var opts = new Dictionary<string, string>
        {
            ["MappingIgnoreSpaces"] = "yes",
            ["MappingIgnoreUnderscores"] = "yes",
        };
        SqlDataCompare.MapKey("[dbo].[Order_Header]", opts)
            .Should().BeEquivalentTo("dbo.OrderHeader");
        SqlDataCompare.MapKey("[dbo].[Order Header]", opts)
            .Should().BeEquivalentTo("dbo.OrderHeader");
    }

    [Fact]
    public void IgnoreWhiteSpace_AppliesLtrimRtrim()
    {
        var opts = new Dictionary<string, string> { ["IgnoreWhiteSpace"] = "yes" };
        var expr = CanonicalPayload.BuildExpression("[Name]", "nvarchar", opts);
        expr.Should().Contain("LTRIM").And.Contain("RTRIM");
    }

    private static CommandRequest MakeRequest(string reportPath) => new(
        Operation: OperationType.DataCompare,
        Source: new Endpoint(EndpointKind.LiveSplit, "Src", "SrcDb"),
        Target: new Endpoint(EndpointKind.LiveSplit, "Tgt", "TgtDb"),
        SyncMode: SyncMode.None,
        SyncFilePath: null,
        ArgFilePath: null,
        CompFilePath: null,
        FilterFilePath: null,
        ReportPath: reportPath,
        LogPath: null,
        ReportFormat: "HTML",
        Quiet: true,
        Argv0: "datacompare",
        Options: new Dictionary<string, string>(),
        Warnings: Array.Empty<string>());
}
