using ArtSync.Abstractions;
using ArtSync.Compat;
using FluentAssertions;

namespace ArtSync.Compat.Tests;

public sealed class CompFileParserTests
{
    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "sample.dcomp");

    [Fact]
    public void SampleDcomp_LoadsConnectionsOptionsAndExclusions()
    {
        var doc = CompFileParser.Load(FixturePath);

        doc.Source!.Kind.Should().Be(EndpointKind.ConnectionString);
        doc.Source.ConnectionString.Should().Contain("example.database.windows.net");
        doc.Target!.ConnectionString.Should().Contain(@"SQLHOST\INSTANCE");

        doc.Options["ExcludeObjectsByMask"].Should().Contain("*tbl_History_*");
        doc.Options["MappingIgnoreSpaces"].Should().Be("yes");
        doc.Options["IgnoreLobColumns"].Should().Be("no");
        doc.Options["IgnoreIdentityColumns"].Should().Be("no");
        doc.Options["DisableForeignKeys"].Should().Be("yes");
        doc.Options["DisableDmlTriggers"].Should().Be("yes");
        doc.Options["UseSchemaNamePrefix"].Should().Be("no");
        doc.Options.Should().NotContainKey("CreateBackupFolder");
        doc.Options.Should().NotContainKey("NeedCompressBackup");

        doc.ExcludedObjectNames.Should().Contain("pr.tbl_BankAccount");
        doc.ExcludedObjectNames.Should().Contain("pr.tbl_PreliminaryReceipts");
        doc.ExcludedObjectNames.Should().NotContain("dbo.tbl_Loans");
        doc.IncludedObjectNames.Should().Contain("dbo.tbl_Loans");
        doc.IncludedObjectNames.Should().Contain("dbo.tbl_Address");
        doc.IncludedObjectNames.Should().NotContain("pr.tbl_BankAccount");
    }

    [Fact]
    public void V6ConnectionModel_ReadsConnectionString()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <CompareDocument>
              <Source Type="Database">
                <ConnectionModel>
                  <PropertyValue Name="ConnectionString" xml:space="preserve">Data Source=example.database.windows.net;Initial Catalog=ExampleDb;User ID=example</PropertyValue>
                </ConnectionModel>
              </Source>
              <Target Type="Database">
                <ConnectionModel>
                  <PropertyValue Name="ConnectionString">Data Source=SQLHOST;Initial Catalog=ExampleDb;User ID=example</PropertyValue>
                </ConnectionModel>
              </Target>
              <Options />
              <SynchronizationOptions />
              <Mapping>
                <ComparedObjects />
              </Mapping>
            </CompareDocument>
            """;
        var tmp = Path.Combine(Path.GetTempPath(), $"artsync-v6-{Guid.NewGuid():N}.dcomp");
        File.WriteAllText(tmp, xml);
        try
        {
            var doc = CompFileParser.Load(tmp);
            doc.Source!.ConnectionString.Should().Contain("example.database.windows.net");
            doc.Target!.ConnectionString.Should().Contain("Data Source=SQLHOST");
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void MissingFile_Throws10()
    {
        var act = () => CompFileParser.Load(Path.Combine(Path.GetTempPath(), "no-such-artsync.dcomp"));
        act.Should().Throw<CompFileException>().Which.ExitCode.Should().Be(10);
    }

    [Fact]
    public void CorruptXml_Throws30()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"artsync-bad-{Guid.NewGuid():N}.dcomp");
        File.WriteAllText(tmp, "<not-a-compare-document/>");
        try
        {
            var act = () => CompFileParser.Load(tmp);
            act.Should().Throw<CompFileException>().Which.ExitCode.Should().Be(30);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Merge_CliConnectionsWin_AndUnionsCheckboxExcludes()
    {
        var req = new CommandRequest(
            Operation: OperationType.DataCompare,
            Source: new Endpoint(EndpointKind.ConnectionString, ConnectionString: "Data Source=cli-src;Initial Catalog=X;Password=p"),
            Target: new Endpoint(EndpointKind.ConnectionString, ConnectionString: "Data Source=cli-tgt;Initial Catalog=X;Password=p"),
            SyncMode: SyncMode.Apply,
            SyncFilePath: null,
            ArgFilePath: null,
            CompFilePath: FixturePath,
            FilterFilePath: null,
            ReportPath: null,
            LogPath: null,
            ReportFormat: null,
            Quiet: false,
            Argv0: "datacompare",
            Options: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ExcludeObjectsByMask"] = "*tbl_Reminder*",
            },
            Warnings: []);

        var merged = CompFileMerger.Apply(req);
        merged.Source!.ConnectionString.Should().Contain("cli-src");
        merged.Target!.ConnectionString.Should().Contain("cli-tgt");
        merged.Options["ExcludeObjectsByMask"].Should().Contain("*tbl_Reminder*");
        merged.Options["ExcludeObjectsByMask"].Should().Contain("pr.tbl_BankAccount");
        merged.Options["ExcludeObjectsByMask"].Should().NotContain("*tbl_History_*");
        merged.Options["IncludeObjectsByMask"].Should().Contain("dbo.tbl_Loans");
        merged.Options["IncludeObjectsByMask"].Should().Contain("dbo.tbl_Address");
        merged.Options["IncludeObjectsByMask"].Should().NotContain("pr.tbl_BankAccount");
        merged.Options["DisableDmlTriggers"].Should().Be("yes");
    }

    [Fact]
    public void Merge_FillsEndpointsFromProject_WhenCliOmitsThem()
    {
        var req = new CommandRequest(
            Operation: OperationType.DataCompare,
            Source: null,
            Target: null,
            SyncMode: SyncMode.None,
            SyncFilePath: null,
            ArgFilePath: null,
            CompFilePath: FixturePath,
            FilterFilePath: null,
            ReportPath: null,
            LogPath: null,
            ReportFormat: null,
            Quiet: false,
            Argv0: "datacompare",
            Options: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Warnings: []);

        var merged = CompFileMerger.Apply(req);
        merged.Source.Should().NotBeNull();
        merged.Target.Should().NotBeNull();
        merged.Warnings.Should().Contain(w => w.Contains("Password=", StringComparison.Ordinal));
    }

    [Fact]
    public void Merge_Scomp_Throws10()
    {
        var req = new CommandRequest(
            Operation: OperationType.SchemaCompare,
            Source: null, Target: null,
            SyncMode: SyncMode.None, SyncFilePath: null, ArgFilePath: null,
            CompFilePath: "schema.scomp",
            FilterFilePath: null, ReportPath: null, LogPath: null, ReportFormat: null,
            Quiet: false, Argv0: "schemacompare",
            Options: new Dictionary<string, string>(),
            Warnings: []);

        var act = () => CompFileMerger.Apply(req);
        act.Should().Throw<CompFileException>().Which.ExitCode.Should().Be(10);
    }

    [Fact]
    public void LocalCapture_TheSyncDcomp_ParsesIfPresent()
    {
        var path = FindUp("_data/TheSync!.dcomp");
        if (path is null) return;

        var doc = CompFileParser.Load(path);
        doc.Source!.ConnectionString.Should().Contain("database.windows.net");
        doc.ExcludedObjectNames.Should().Contain("pr.tbl_BankAccount");
        doc.IncludedObjectNames.Should().HaveCount(123);
        doc.IncludedObjectNames.Should().Contain("dbo.tbl_Loans");
        doc.IncludedObjectNames.Should().NotContain("UW.tbl_Loans");
        doc.Options["ExcludeObjectsByMask"].Should().Contain("tbl_EmailNotificationQueue");
        doc.Options["DisableForeignKeys"].Should().Be("yes");
        doc.Options.Should().NotContainKey("CreateBackupFolder");
    }

    private static string? FindUp(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
