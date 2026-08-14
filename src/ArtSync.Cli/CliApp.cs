using ArtSync.Abstractions;
using ArtSync.Compat;

namespace ArtSync.Cli;

/// <summary>
/// argv[0] dispatch, help, exit-code printing, and routing to an
/// <see cref="IOperationHandler"/>.  All heavy logic lives in Compat and
/// future engine projects; this class stays thin (SPEC §15).
/// </summary>
public sealed class CliApp
{
    private readonly IArgvParser _parser;
    private readonly IOperationHandler _handler;
    private readonly TextWriter _out;
    private readonly TextWriter _err;

    public CliApp(
        IArgvParser parser,
        IOperationHandler handler,
        TextWriter? stdout = null,
        TextWriter? stderr = null)
    {
        _parser = parser;
        _handler = handler;
        _out = stdout ?? Console.Out;
        _err = stderr ?? Console.Error;
    }

    /// <summary>
    /// Factory for the real production wiring (used by Program.cs).
    /// SchemaCompare → DacFx engine (Phase 2).
    /// DataCompare   → NotImplementedHandler (Phase 3 will replace this).
    /// </summary>
    public static CliApp CreateDefault()
    {
        var loader = new ArgFileLoader();
        var parser = new ArgvParser(loader);

        var redactor = new SecretRedactor();
        ArtSync.Abstractions.IOperationLogger LoggerFactory(string path)
            => ArtSync.Reporting.FileOperationLogger.Open(path, redactor);
        var reporter = new ArtSync.Reporting.HtmlXmlCsvReporter(redactor);

        var schemaHandler = new ArtSync.Schema.SchemaOperationHandler(
            new ArtSync.Schema.DacFxSchemaCompare(),
            LoggerFactory,
            reporter);
        var dataHandler = new ArtSync.Data.DataOperationHandler(
            new ArtSync.Data.SqlDataCompare(),
            LoggerFactory,
            reporter);

        var dispatcher = new DispatchingHandler(schemaHandler, dataHandler);
        return new CliApp(parser, dispatcher);
    }

    /// <summary>
    /// Main entry point.  Returns the process exit code.
    /// </summary>
    public int Run(IReadOnlyList<string> argv, string argv0)
    {
        var result = _parser.Parse(argv, argv0);

        if (result is ParseResult.Failure failure)
        {
            _err.WriteLine($"Error: {failure.Message}");
            return failure.ExitCode;
        }

        var req = ((ParseResult.Success)result).Request;

        if (!req.Quiet)
        {
            foreach (var w in req.Warnings)
                _err.WriteLine($"Warning: {w}");
        }

        return req.Operation switch
        {
            OperationType.Help        => PrintHelp(),
            OperationType.ExitCodes   => PrintExitCodes(),
            OperationType.Activate
            or OperationType.Deactivate => 0,
            OperationType.Unsupported =>
                Fail($"Operation is not supported by ArtSync. Run /? for usage."),
            _ => RunHandler(req),
        };
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private int PrintHelp()
    {
        _out.WriteLine(HelpText.General);
        return 0;
    }

    private int PrintExitCodes()
    {
        _out.WriteLine(HelpText.ExitCodes);
        return 0;
    }

    private int Fail(string message)
    {
        _err.WriteLine($"Error: {message}");
        return 10;
    }

    private int RunHandler(CommandRequest req)
    {
        try
        {
            var opResult = _handler.Run(req);
            if (opResult.Message is { Length: > 0 } msg)
            {
                bool success = opResult.ExitCode is 0 or 100 or 101;
                if (req.Quiet)
                {
                    if (!success)
                        _err.WriteLine($"Error: {msg}");
                }
                else if (success)
                    _out.WriteLine(msg);
                else
                    _err.WriteLine($"Error: {msg}");
            }
            return opResult.ExitCode;
        }
        catch (Exception ex)
        {
            _err.WriteLine($"Fatal: {ex.Message}");
            return 10;
        }
    }
}
