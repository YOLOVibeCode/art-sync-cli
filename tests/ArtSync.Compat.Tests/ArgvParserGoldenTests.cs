using ArtSync.Abstractions;
using ArtSync.Compat;
using FluentAssertions;

namespace ArtSync.Compat.Tests;

/// <summary>
/// Golden-case parser tests driven by the documented Devart CLI examples in
/// docs/cli-examples.md and the ten TDD cases listed in the implementation plan.
///
/// No databases, no file system. An empty IArgFileLoader stub is used for all
/// tests that do not exercise /argfile.
/// </summary>
public sealed class ArgvParserGoldenTests
{
    private static readonly IArgFileLoader NoOpLoader = new FakeArgFileLoader();
    private static ArgvParser Parser() => new(NoOpLoader);

    // ─── TDD case 1: Schema — connection strings — apply (cli-examples §1) ──

    [Fact]
    public void Schema_ConnectionStrings_Apply_ShouldSucceed()
    {
        var argv = new[]
        {
            "/schemacompare",
            "/source",
            @"connection:Data Source=demo-mssql\SQLEXPRESS02;Encrypt=False;Initial Catalog=AdventureWorks2022Dev;Integrated Security=False;User ID=JordanS",
            "/target",
            @"connection:Data Source=demo-mssql\SQLEXPRESS01;Encrypt=False;Initial Catalog=AdventureWorks2022Test;Integrated Security=False;User ID=JordanS",
            "/MappingIgnoreSpaces:Yes",
            "/MappingIgnoreCase:Yes",
            "/sync",
        };

        var result = Parser().Parse(argv, "dbforgesql");

        var req = AssertSuccess(result);
        req.Operation.Should().Be(OperationType.SchemaCompare);
        req.Source.Should().NotBeNull();
        req.Source!.Kind.Should().Be(EndpointKind.ConnectionString);
        req.Source.ConnectionString.Should().Contain("AdventureWorks2022Dev");
        req.Target.Should().NotBeNull();
        req.Target!.Kind.Should().Be(EndpointKind.ConnectionString);
        req.Target.ConnectionString.Should().Contain("AdventureWorks2022Test");
        req.SyncMode.Should().Be(SyncMode.Apply);
        req.Options.Should().ContainKey("MappingIgnoreSpaces").WhoseValue.Should().Be("Yes");
        req.Options.Should().ContainKey("MappingIgnoreCase").WhoseValue.Should().Be("Yes");
    }

    // ─── TDD case 2: Schema — split server/database/user/password — script ──

    [Fact]
    public void Schema_SplitParams_ScriptFile_ShouldSucceed()
    {
        var argv = new[]
        {
            "/schemacompare",
            "/source", "server:SqlServer1", "user:sa", "password:sa", "database:db1",
            "/target", "server:SqlServer2", "user:sa", "password:sa", "database:db2",
            @"/sync:D:\compare_result.sql",
        };

        var result = Parser().Parse(argv, "dbforgesql");

        var req = AssertSuccess(result);
        req.Source!.Kind.Should().Be(EndpointKind.LiveSplit);
        req.Source.Server.Should().Be("SqlServer1");
        req.Source.Database.Should().Be("db1");
        req.Source.User.Should().Be("sa");
        req.Source.Password.Should().Be("sa");
        req.Target!.Server.Should().Be("SqlServer2");
        req.SyncMode.Should().Be(SyncMode.ScriptFile);
        req.SyncFilePath.Should().Be(@"D:\compare_result.sql");
    }

    // ─── TDD case 3: /argfile + CLI override — CLI value wins ───────────────

    [Fact]
    public void ArgFile_CliOverrideWins()
    {
        // Argfile says compare-only; CLI adds /sync (apply). CLI wins.
        var fileLoader = new FakeArgFileLoader(new Dictionary<string, IReadOnlyList<string>>
        {
            [@"D:\args.txt"] = new[]
            {
                "/schemacompare",
                "/source", "server:FileServer", "database:db1",
                "/target", "server:FileServer", "database:db2",
            },
        });
        var parser = new ArgvParser(fileLoader);

        var argv = new[] { @"/argfile:D:\args.txt", "/sync" };

        var result = parser.Parse(argv, "dbforgesql");

        var req = AssertSuccess(result);
        req.SyncMode.Should().Be(SyncMode.Apply);
        req.Source!.Server.Should().Be("FileServer");
        req.ArgFilePath.Should().Be(@"D:\args.txt");
    }

    // ─── TDD case 4: Boolean vocabulary synonyms ────────────────────────────

    [Theory]
    [InlineData("/IgnoreForeignKeys:yes",   "yes")]
    [InlineData("/IgnoreForeignKeys:Yes",   "Yes")]
    [InlineData("/IgnoreForeignKeys:Y",     "Y")]
    [InlineData("/IgnoreForeignKeys:On",    "On")]
    [InlineData("/IgnoreForeignKeys:True",  "True")]
    [InlineData("/IgnoreForeignKeys:T",     "T")]
    [InlineData("/IgnoreForeignKeys:no",    "no")]
    [InlineData("/IgnoreForeignKeys:No",    "No")]
    [InlineData("/IgnoreForeignKeys:N",     "N")]
    [InlineData("/IgnoreForeignKeys:Off",   "Off")]
    [InlineData("/IgnoreForeignKeys:False", "False")]
    [InlineData("/IgnoreForeignKeys:F",     "F")]
    public void BooleanVocabulary_AllVariants_Accepted(string switchToken, string expectedValue)
    {
        var argv = new[] { "/schemacompare", switchToken };
        var result = Parser().Parse(argv, "dbforgesql");

        var req = AssertSuccess(result);
        req.Options.Should().ContainKey("IgnoreForeignKeys").WhoseValue.Should().Be(expectedValue);
    }

    // ─── TDD case 4b: Short and long option names are equivalent ─────────────

    [Fact]
    public void ShortAndLongOptionNames_BothAccepted()
    {
        // Short name: icase → canonical IgnoreCase
        var r1 = Parser().Parse(new[] { "/schemacompare", "/icase:yes" }, "dbforgesql");
        AssertSuccess(r1).Options.Should().ContainKey("IgnoreCase");

        // Long name: IgnoreCase → canonical IgnoreCase
        var r2 = Parser().Parse(new[] { "/schemacompare", "/IgnoreCase:yes" }, "dbforgesql");
        AssertSuccess(r2).Options.Should().ContainKey("IgnoreCase");
    }

    // ─── TDD case 5: argv[0] dispatch ────────────────────────────────────────

    [Fact]
    public void Argv0_SchemaCompare_ImpliesOperation_NoSwitchNeeded()
    {
        var argv = new[]
        {
            "/source", "server:S1", "database:db1",
            "/target", "server:S2", "database:db2",
            "/sync",
        };

        var result = Parser().Parse(argv, "schemacompare");

        AssertSuccess(result).Operation.Should().Be(OperationType.SchemaCompare);
    }

    [Fact]
    public void Argv0_SchemaCompare_ExplicitSwitchAlsoAccepted()
    {
        var argv = new[]
        {
            "/schemacompare",
            "/source", "server:S1", "database:db1",
            "/target", "server:S2", "database:db2",
        };

        AssertSuccess(Parser().Parse(argv, "schemacompare")).Operation
            .Should().Be(OperationType.SchemaCompare);
    }

    [Fact]
    public void Argv0_Dbforgesql_RequiresOperationSwitch()
    {
        var argv = new[] { "/source", "server:S1", "database:db1" };

        var result = Parser().Parse(argv, "dbforgesql");

        result.Should().BeOfType<ParseResult.Failure>()
            .Which.ExitCode.Should().Be(10);
    }

    [Fact]
    public void Argv0_DataCompare_ImpliesOperation()
    {
        var argv = new[]
        {
            "/source", "server:S1", "database:db1",
            "/target", "server:S2", "database:db2",
        };

        AssertSuccess(Parser().Parse(argv, "datacompare")).Operation
            .Should().Be(OperationType.DataCompare);
    }

    [Fact]
    public void Argv0_DataCompare_RejectsSchemaCompareSwitch()
    {
        var argv = new[] { "/schemacompare", "/source", "server:S", "database:D" };

        Parser().Parse(argv, "datacompare").Should().BeOfType<ParseResult.Failure>()
            .Which.ExitCode.Should().Be(10);
    }

    // ─── TDD case 6: Out-of-scope operations → exit 10; /activate → 0 ───────

    [Theory]
    [InlineData("/dataexport")]
    [InlineData("/script")]
    [InlineData("/scriptsfolder")]
    [InlineData("/snapshot")]
    [InlineData("/dataimport")]
    [InlineData("/generatedata")]
    [InlineData("/document")]
    [InlineData("/datareport")]
    [InlineData("/formatsql")]
    [InlineData("/findinvalidobjects")]
    [InlineData("/testsupport")]
    public void UnsupportedOperations_ReturnExit10(string op)
    {
        var result = Parser().Parse(new[] { op }, "dbforgesql");

        result.Should().BeOfType<ParseResult.Failure>()
            .Which.ExitCode.Should().Be(10);
    }

    [Fact]
    public void Activate_ReturnsSuccess()
    {
        var result = Parser().Parse(new[] { "/activate" }, "dbforgesql");

        AssertSuccess(result).Operation.Should().Be(OperationType.Activate);
    }

    [Fact]
    public void Deactivate_ReturnsSuccess()
    {
        AssertSuccess(Parser().Parse(new[] { "/deactivate" }, "dbforgesql"))
            .Operation.Should().Be(OperationType.Deactivate);
    }

    // ─── TDD case 7: /source backup: → exit 10 ───────────────────────────────

    [Fact]
    public void Source_BackupEndpoint_ReturnsExit10()
    {
        var argv = new[]
        {
            "/datacompare",
            "/source", @"backup:D:\backup_file.bak",
            "/target", "server:TargetServer", "database:TargetDB", "user:sa", "password:",
        };

        var result = Parser().Parse(argv, "dbforgesql");

        result.Should().BeOfType<ParseResult.Failure>()
            .Which.ExitCode.Should().Be(10);
    }

    // ─── TDD case 8: Forum bug — missing slash → exit 10 (cli-examples §6) ──

    [Fact]
    public void ForumBug_MissingSlashBeforeLog_ReturnsExit10()
    {
        // /sync log:"path"  — "log:" is not prefixed with '/', so it looks like
        // a sub-parameter outside any /source //target block.
        var argv = new[]
        {
            "/schemacompare",
            @"/compfile:C:\Comparisons\operations.scomp",
            "/sync",
            @"log:C:\Comparisons\Logs\operations.log",   // ← missing '/'
        };

        var result = Parser().Parse(argv, "schemacompare");

        result.Should().BeOfType<ParseResult.Failure>()
            .Which.ExitCode.Should().Be(10);
    }

    // ─── TDD case 9: Common switches accepted ─────────────────────────────────

    [Fact]
    public void CommonSwitches_QuietReportLogCompfile_AllAccepted()
    {
        var argv = new[]
        {
            "/schemacompare",
            "/compfile:file_name.scomp",
            "/q",
            "/report:report.html",
            "/reportformat:HTML",
            "/log:D:\\sync.log",
        };

        var req = AssertSuccess(Parser().Parse(argv, "dbforgesql"));
        req.Quiet.Should().BeTrue();
        req.ReportPath.Should().Be("report.html");
        req.ReportFormat.Should().Be("HTML");
        req.LogPath.Should().Be("D:\\sync.log");
        req.CompFilePath.Should().Be("file_name.scomp");
    }

    [Fact]
    public void CompareOnly_NoSync_SyncModeIsNone()
    {
        var argv = new[] { "/schemacompare", "/source", "server:S1", "database:D1", "/target", "server:S2", "database:D2" };
        AssertSuccess(Parser().Parse(argv, "dbforgesql")).SyncMode.Should().Be(SyncMode.None);
    }

    // ─── TDD case 10: /? and /exitcodes → success ────────────────────────────

    [Fact]
    public void HelpSwitch_ReturnsHelpOperation()
    {
        AssertSuccess(Parser().Parse(new[] { "/schemacompare", "/?" }, "dbforgesql"))
            .Operation.Should().Be(OperationType.Help);

        // Bare /? also works.
        AssertSuccess(Parser().Parse(new[] { "/?" }, "schemacompare"))
            .Operation.Should().Be(OperationType.Help);
    }

    [Fact]
    public void ExitCodesSwitch_ReturnsExitCodesOperation()
    {
        AssertSuccess(Parser().Parse(new[] { "/datacompare", "/exitcodes" }, "dbforgesql"))
            .Operation.Should().Be(OperationType.ExitCodes);

        AssertSuccess(Parser().Parse(new[] { "/datacompare", "/exitcodes" }, "datacompare"))
            .Operation.Should().Be(OperationType.ExitCodes);
    }

    // ─── Additional documented examples from cli-examples.md ─────────────────

    [Fact]
    public void Schema_Apply_PlusLog_ShouldSucceed()  // cli-examples §3
    {
        var argv = new[]
        {
            "/schemacompare",
            "/source", "server:SqlServer1", "user:sa", "password:sa", "database:db1",
            "/target", "server:SqlServer2", "user:sa", "password:sa", "database:db2",
            "/sync",
            "/log:D:\\sync.log",
        };

        var req = AssertSuccess(Parser().Parse(argv, "schemacompare"));
        req.SyncMode.Should().Be(SyncMode.Apply);
        req.LogPath.Should().Be("D:\\sync.log");
    }

    [Fact]
    public void Schema_CompfileAndOptions_ShouldSucceed()  // cli-examples §5
    {
        var argv = new[]
        {
            "/schemacompare",
            "/compfile:file_name.scomp",
            "/icase:yes",
            "/IgnoreForeignKeys:yes",
            "/report:report.html",
            "/reportformat:HTML",
            "/groupby:objecttype",
            "/incsettings:T",
            "/sync",
        };

        var req = AssertSuccess(Parser().Parse(argv, "dbforgesql"));
        req.Options.Should().ContainKey("IgnoreCase").WhoseValue.Should().Be("yes");
        req.Options.Should().ContainKey("IgnoreForeignKeys").WhoseValue.Should().Be("yes");
        req.SyncMode.Should().Be(SyncMode.Apply);
        req.Warnings.Should().NotBeEmpty(); // groupby and incsettings produce warnings
    }

    [Fact]
    public void Schema_IgnorePermissions_ShouldSucceed()  // cli-examples §7
    {
        var argv = new[]
        {
            "/schemacompare",
            "/source",
            @"connection:Data Source=demo-mssql\SQLEXPRESS02;Initial Catalog=BicycleStoreDev;Integrated Security=False;User ID=sa",
            "/target",
            @"connection:Data Source=demo-mssql\SQLEXPRESS02;Initial Catalog=BicycleStoreProd;Integrated Security=False;User ID=sa",
            "/IgnorePermissions:Yes",
            "/IgnoreUserPermissions:Yes",
            "/sync",
        };

        var req = AssertSuccess(Parser().Parse(argv, "dbforgesql.com"));
        req.Options.Should().ContainKey("IgnorePermissions").WhoseValue.Should().Be("Yes");
        req.Options.Should().ContainKey("IgnoreUserPermissions").WhoseValue.Should().Be("Yes");
    }

    [Fact]
    public void Schema_Filter_ShouldParseFilterPath()  // cli-examples §8
    {
        var argv = new[]
        {
            "/schemacompare",
            "/source",
            @"connection:Data Source=demo-mssql\SQLEXPRESS02;Initial Catalog=BicycleStoreDev;Integrated Security=False;User ID=sa",
            "/target",
            @"connection:Data Source=demo-mssql\SQLEXPRESS02;Initial Catalog=BicycleStoreProd;Integrated Security=False;User ID=sa",
            "/IgnorePermissions:Yes",
            "/IgnoreUserPermissions:Yes",
            @"/filter:C:\jordansanders\Custom.scflt",
            "/sync",
        };

        var req = AssertSuccess(Parser().Parse(argv, "dbforgesql.com"));
        req.FilterFilePath.Should().Be(@"C:\jordansanders\Custom.scflt");
    }

    [Fact]
    public void Data_ConnectionStrings_Apply_PlusLog()  // cli-examples §13
    {
        var argv = new[]
        {
            "/datacompare",
            "/source",
            "connection:Connect Timeout=120;Data Source=<source>;Initial Catalog=<sdb>;Integrated Security=False;User ID=<u>;Password=<p>;Pooling=False",
            "/target",
            "connection:Connect Timeout=120;Data Source=<target>;Initial Catalog=<tdb>;Integrated Security=False;User ID=<u>;Password=<p>;Pooling=False",
            "/sync",
            "/log:D:\\sync.log",
        };

        var req = AssertSuccess(Parser().Parse(argv, "dbforgesql.com"));
        req.Operation.Should().Be(OperationType.DataCompare);
        req.SyncMode.Should().Be(SyncMode.Apply);
        req.Source!.Kind.Should().Be(EndpointKind.ConnectionString);
    }

    [Fact]
    public void Data_MappingIgnoreUnderscores_ShouldSucceed()  // cli-examples §13 variant
    {
        var argv = new[]
        {
            "/datacompare",
            "/source",
            @"connection:Data Source=demo-mssql\SQLEXPRESS02;Encrypt=False;Initial Catalog=AdventureWorks2022Dev;Integrated Security=False;User ID=JordanS",
            "/target",
            @"connection:Data Source=demo-mssql\SQLEXPRESS01;Encrypt=False;Initial Catalog=AdventureWorks2022Test;Integrated Security=False;User ID=JordanS",
            "/MappingIgnoreCase:Yes",
            "/MappingIgnoreUnderscores:Yes",
            @"/sync:C:\Users\JordanS\Desktop\AdventureWorks2022 (development) vs. AdventureWorks2022 (production).sql",
        };

        var req = AssertSuccess(Parser().Parse(argv, "dbforgesql.com"));
        req.Options.Should().ContainKey("MappingIgnoreUnderscores");
        req.SyncMode.Should().Be(SyncMode.ScriptFile);
        req.SyncFilePath.Should().Contain("AdventureWorks2022");
    }

    [Fact]
    public void Data_FsPath_ReturnsExit10()  // cli-examples §16 — v1 unsupported
    {
        var argv = new[]
        {
            "/datacompare",
            @"/compfile:D:\workDir\DC1vsDC2.dcomp",
            @"/fspath:\\SqlHost\Temp",
            "/sync",
        };

        Parser().Parse(argv, "dbforgesql.com")
            .Should().BeOfType<ParseResult.Failure>()
            .Which.ExitCode.Should().Be(10);
    }

    [Fact]
    public void Data_DisableNocommentsNodml_ShouldSucceed()  // cli-examples §14
    {
        var argv = new[]
        {
            "/datacompare",
            "/compfile:DC1vsDC2.dcomp",
            "/icase:yes",
            "/report:report.html",
            "/reportformat:HTML",
            "/sync",
        };

        var req = AssertSuccess(Parser().Parse(argv, "datacompare"));
        req.Options.Should().ContainKey("IgnoreCase");
    }

    [Fact]
    public void Data_NocommentNodml_ShouldSucceed()  // cli-examples §14 second variant
    {
        var argv = new[]
        {
            "/datacompare",
            "/compfile:file.dcomp",
            "/nocomments:yes",
            "/nodml:yes",
            "/report:D:\\report.html",
            "/reportformat:HTML",
            "/sync",
        };

        var req = AssertSuccess(Parser().Parse(argv, "dbforgesql.com"));
        req.Options.Should().ContainKey("ExcludeComments");
        req.Options.Should().ContainKey("DisableDmlTriggers");
    }

    [Fact]
    public void Schema_Execute_ReturnsExit10()  // cli-examples §9 execute lines → exit 10
    {
        var argv = new[]
        {
            "/execute",
            @"/connection:%user connection%",
            @"/inputfile D:\Temp\DevOps\Create.sql",
        };

        Parser().Parse(argv, "dbforgesql.com")
            .Should().BeOfType<ParseResult.Failure>()
            .Which.ExitCode.Should().Be(10);
    }

    [Fact]
    public void UnknownSwitch_ReturnsExit10()
    {
        var result = Parser().Parse(new[] { "/schemacompare", "/totally-unknown-switch" }, "dbforgesql");
        result.Should().BeOfType<ParseResult.Failure>().Which.ExitCode.Should().Be(10);
    }

    [Fact]
    public void DuplicateOperationSwitch_SameOp_IsAccepted()
    {
        // Devart docs mention that passing /schemacompare on schemacompare.com is fine.
        var argv = new[] { "/schemacompare", "/schemacompare", "/source", "server:S", "database:D", "/target", "server:T", "database:D2" };
        AssertSuccess(Parser().Parse(argv, "schemacompare")).Operation
            .Should().Be(OperationType.SchemaCompare);
    }

    [Fact]
    public void Schema_MixedConnectionAndSplitParams_ReturnsExit11()
    {
        var argv = new[]
        {
            "/schemacompare",
            "/source", "connection:Data Source=S", "server:S2",
        };

        Parser().Parse(argv, "dbforgesql")
            .Should().BeOfType<ParseResult.Failure>()
            .Which.ExitCode.Should().Be(11);
    }

    // ─── Helper ───────────────────────────────────────────────────────────────

    private static CommandRequest AssertSuccess(ParseResult result)
    {
        result.Should().BeOfType<ParseResult.Success>(
            because: result is ParseResult.Failure f ? $"parse failed: exit {f.ExitCode} — {f.Message}" : "");
        return ((ParseResult.Success)result).Request;
    }
}

// ─── Test doubles ─────────────────────────────────────────────────────────────

internal sealed class FakeArgFileLoader : IArgFileLoader
{
    private readonly Dictionary<string, IReadOnlyList<string>> _files;

    public FakeArgFileLoader(Dictionary<string, IReadOnlyList<string>>? files = null)
        => _files = files ?? new Dictionary<string, IReadOnlyList<string>>();

    public IReadOnlyList<string> Load(string path)
    {
        if (_files.TryGetValue(path, out var tokens)) return tokens;
        throw new FileNotFoundException($"Fake: file not registered: {path}");
    }
}
