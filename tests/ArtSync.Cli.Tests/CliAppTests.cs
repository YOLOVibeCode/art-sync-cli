using ArtSync.Abstractions;
using ArtSync.Cli;
using ArtSync.Compat;
using FluentAssertions;

namespace ArtSync.Cli.Tests;

/// <summary>
/// Tests for argv[0] dispatch, /?, /exitcodes, /activate, and
/// NotImplementedHandler routing.  No SQL connections.
/// </summary>
public sealed class CliAppTests
{
    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static CliApp BuildApp(IOperationHandler? handler = null)
    {
        var parser = new ArgvParser(new NoOpArgFileLoader());
        return new CliApp(
            parser,
            handler ?? new NotImplementedHandler(),
            stdout: TextWriter.Null,
            stderr: TextWriter.Null);
    }

    private static CliApp BuildAppWithOutput(
        IOperationHandler? handler,
        StringWriter stdout,
        StringWriter stderr)
    {
        var parser = new ArgvParser(new NoOpArgFileLoader());
        return new CliApp(parser, handler ?? new NotImplementedHandler(), stdout, stderr);
    }

    // ─── /? → exit 0, help printed ───────────────────────────────────────────

    [Fact]
    public void HelpSwitch_OnSchemaCompare_Returns0()
    {
        var code = BuildApp().Run(new[] { "/schemacompare", "/?" }, "dbforgesql");
        code.Should().Be(0);
    }

    [Fact]
    public void HelpSwitch_BareOnSchemaCompareExe_Returns0()
    {
        BuildApp().Run(new[] { "/?" }, "schemacompare").Should().Be(0);
    }

    [Fact]
    public void HelpSwitch_PrintsTextToStdout()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        BuildAppWithOutput(null, stdout, stderr).Run(new[] { "/?" }, "schemacompare");

        stdout.ToString().Should().Contain("ArtSync");
    }

    // ─── /exitcodes → exit 0, table printed ──────────────────────────────────

    [Fact]
    public void ExitCodes_Returns0()
    {
        BuildApp().Run(new[] { "/datacompare", "/exitcodes" }, "dbforgesql").Should().Be(0);
    }

    [Fact]
    public void ExitCodes_PrintsTable()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        BuildAppWithOutput(null, stdout, stderr)
            .Run(new[] { "/datacompare", "/exitcodes" }, "datacompare");

        var text = stdout.ToString();
        text.Should().Contain("100");
        text.Should().Contain("101");
        text.Should().Contain("112");
    }

    // ─── /activate → exit 0 (no-op license) ──────────────────────────────────

    [Fact]
    public void Activate_Returns0()
    {
        BuildApp().Run(new[] { "/activate" }, "dbforgesql").Should().Be(0);
    }

    [Fact]
    public void Deactivate_Returns0()
    {
        BuildApp().Run(new[] { "/deactivate" }, "dbforgesql").Should().Be(0);
    }

    // ─── argv[0] implied operation dispatch ───────────────────────────────────

    [Fact]
    public void SchemaCompareExe_WithoutOperationSwitch_RoutesToHandler()
    {
        // NotImplementedHandler returns 10, confirming the handler was called.
        var code = BuildApp().Run(
            new[] { "/source", "server:S", "database:D", "/target", "server:T", "database:D2" },
            "schemacompare");

        code.Should().Be(10); // NotImplementedHandler
    }

    [Fact]
    public void DataCompareExe_WithoutOperationSwitch_RoutesToHandler()
    {
        var code = BuildApp().Run(
            new[] { "/source", "server:S", "database:D", "/target", "server:T", "database:D2" },
            "datacompare");

        code.Should().Be(10);
    }

    // ─── Parse errors return exit 10 ─────────────────────────────────────────

    [Fact]
    public void ParseFailure_ReturnsCorrectExitCode()
    {
        // No operation on dbforgesql → parse error exit 10.
        BuildApp().Run(new[] { "/source", "server:S" }, "dbforgesql").Should().Be(10);
    }

    [Fact]
    public void UnknownSwitch_Returns10()
    {
        BuildApp().Run(new[] { "/schemacompare", "/bogus-flag" }, "dbforgesql").Should().Be(10);
    }

    [Fact]
    public void UnsupportedOperation_Returns10()
    {
        BuildApp().Run(new[] { "/dataexport" }, "dbforgesql").Should().Be(10);
    }

    // ─── NotImplementedHandler round-trip ────────────────────────────────────

    [Fact]
    public void SchemaCompare_WithEndpoints_RoutesToNotImplementedHandler_Returns10()
    {
        var code = BuildApp().Run(
            new[]
            {
                "/schemacompare",
                "/source", "server:SqlServer1", "user:sa", "password:sa", "database:db1",
                "/target", "server:SqlServer2", "user:sa", "password:sa", "database:db2",
                "/sync",
            },
            "dbforgesql");

        code.Should().Be(10);
    }

    // ─── Custom handler integration ───────────────────────────────────────────

    [Fact]
    public void Quiet_SuppressesSuccessStdout()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var handler = new StubHandler(100, message: "identical");
        BuildAppWithOutput(handler, stdout, stderr).Run(
            new[] { "/schemacompare", "/q", "/source", "server:S", "database:D", "/target", "server:T", "database:D2" },
            "dbforgesql");

        stdout.ToString().Should().BeEmpty();
    }

    [Fact]
    public void CustomHandler_ReturnsItsExitCode()
    {
        var handler = new StubHandler(100);
        var code = BuildApp(handler).Run(
            new[] { "/schemacompare", "/source", "server:S", "database:D", "/target", "server:T", "database:D2" },
            "dbforgesql");

        code.Should().Be(100);
        handler.WasCalled.Should().BeTrue();
    }

    [Fact]
    public void CustomHandler_ReceivesCorrectRequest()
    {
        CommandRequest? captured = null;
        var handler = new StubHandler(0, req => captured = req);

        BuildApp(handler).Run(
            new[] { "/schemacompare", "/source", "server:SrcServer", "database:SrcDb", "/target", "server:TgtServer", "database:TgtDb" },
            "dbforgesql");

        captured.Should().NotBeNull();
        captured!.Operation.Should().Be(OperationType.SchemaCompare);
        captured.Source!.Server.Should().Be("SrcServer");
        captured.Target!.Server.Should().Be("TgtServer");
    }
}

// ─── Test doubles ─────────────────────────────────────────────────────────────

internal sealed class NoOpArgFileLoader : IArgFileLoader
{
    public IReadOnlyList<string> Load(string path)
        => throw new FileNotFoundException($"No argfile in tests: {path}");
}

internal sealed class StubHandler : IOperationHandler
{
    private readonly int _exitCode;
    private readonly Action<CommandRequest>? _onRun;
    private readonly string? _message;
    public bool WasCalled { get; private set; }

    public StubHandler(int exitCode, Action<CommandRequest>? onRun = null, string? message = null)
    {
        _exitCode = exitCode;
        _onRun = onRun;
        _message = message;
    }

    public OperationResult Run(CommandRequest request)
    {
        WasCalled = true;
        _onRun?.Invoke(request);
        return new OperationResult(_exitCode, _message);
    }
}
