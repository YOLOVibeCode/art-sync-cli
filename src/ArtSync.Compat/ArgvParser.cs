using ArtSync.Abstractions;

namespace ArtSync.Compat;

/// <summary>
/// Devart-compatible argv parser.  Handles the non-POSIX grammar documented in SPEC §5
/// including grouped /source /target sub-parameters, /argfile merging (CLI wins), and
/// the full option registry from <see cref="KnownOptions"/>.
/// </summary>
public sealed class ArgvParser : IArgvParser
{
    private readonly IArgFileLoader _argFileLoader;

    public ArgvParser(IArgFileLoader argFileLoader)
    {
        _argFileLoader = argFileLoader;
    }

    // ── Public entry point ────────────────────────────────────────────────────

    public ParseResult Parse(IReadOnlyList<string> argv, string argv0)
    {
        var impliedOp = NormalizeArgv0(argv0);

        // Separate /argfile token(s) from the rest of the CLI args.
        string? argFilePath = null;
        var cliTokens = new List<string>(argv.Count);

        foreach (var tok in argv)
        {
            if (TrySplitSwitch(tok, out var sn, out var sv) &&
                sn.Equals("argfile", StringComparison.OrdinalIgnoreCase))
            {
                if (sv is null)
                    return ParseResult.Fail(10, "/argfile requires a path value: /argfile:<path>");
                argFilePath = sv;
            }
            else
            {
                cliTokens.Add(tok);
            }
        }

        // Build effective token stream: argfile first, then CLI overrides.
        var effective = new List<string>();
        if (argFilePath is not null)
        {
            try
            {
                effective.AddRange(_argFileLoader.Load(argFilePath)
                    .Where(t => !IsSwitch(t, "argfile"))); // no argfile chaining
            }
            catch (FileNotFoundException)
            {
                return ParseResult.Fail(10, $"/argfile: file not found: {argFilePath}");
            }
            catch (Exception ex)
            {
                return ParseResult.Fail(10, $"/argfile: cannot read '{argFilePath}': {ex.Message}");
            }
        }
        effective.AddRange(cliTokens);

        return ParseTokens(effective, impliedOp, argv0, argFilePath);
    }

    // ── Token-stream parser ───────────────────────────────────────────────────

    private static ParseResult ParseTokens(
        List<string> tokens, OperationType impliedOp, string argv0, string? argFilePath)
    {
        var state = new ParseState { ImpliedOperation = impliedOp, ArgFilePath = argFilePath };

        for (int i = 0; i < tokens.Count;)
        {
            var tok = tokens[i];

            if (tok.StartsWith('/'))
            {
                state.EndCurrentEndpoint();
                var err = ProcessSwitch(tok, ref i, state);
                if (err is not null) return err;
            }
            else
            {
                // Sub-parameter — only valid inside /source or /target block.
                if (state.ActiveEndpoint is null)
                    return ParseResult.Fail(10,
                        $"Unexpected token '{tok}'. Switch names must start with '/'. " +
                        $"Did you mean '/{tok}'?");

                var err = state.ActiveEndpoint.AddSubParam(tok);
                if (err is not null) return err;
                i++;
            }
        }

        state.EndCurrentEndpoint();
        return BuildResult(state, argv0);
    }

    // ── Switch dispatcher ─────────────────────────────────────────────────────

    private static ParseResult? ProcessSwitch(string token, ref int i, ParseState state)
    {
        TrySplitSwitch(token, out var switchName, out var inlineValue);
        var switchLower = switchName.ToLowerInvariant();

        switch (switchLower)
        {
            // ── Operation switches ────────────────────────────────────────────
            case "schemacompare":
            {
                if (state.ImpliedOperation == OperationType.DataCompare)
                    return ParseResult.Fail(10,
                        "Operation /schemacompare conflicts with executable 'datacompare'.");
                if (state.Operation is not OperationType.Unknown and not OperationType.SchemaCompare)
                    return ParseResult.Fail(11, "Duplicate or conflicting operation switch.");
                state.Operation = OperationType.SchemaCompare;
                i++;
                return null;
            }

            case "datacompare":
            {
                if (state.ImpliedOperation == OperationType.SchemaCompare)
                    return ParseResult.Fail(10,
                        "Operation /datacompare conflicts with executable 'schemacompare'.");
                if (state.Operation is not OperationType.Unknown and not OperationType.DataCompare)
                    return ParseResult.Fail(11, "Duplicate or conflicting operation switch.");
                state.Operation = OperationType.DataCompare;
                i++;
                return null;
            }

            case "activate":
                state.Operation = OperationType.Activate;
                i++;
                return null;

            case "deactivate":
                state.Operation = OperationType.Deactivate;
                i++;
                return null;

            case "?":
                state.Operation = OperationType.Help;
                i++;
                return null;

            case "exitcodes":
                state.Operation = OperationType.ExitCodes;
                i++;
                return null;

            // ── Endpoint switches ─────────────────────────────────────────────
            case "source":
                if (inlineValue is not null)
                    return ParseResult.Fail(10,
                        "/source takes no inline value; use sub-parameters: /source server:S database:D user:U password:P");
                state.ActiveEndpoint = new EndpointBuilder(isSource: true);
                i++;
                return null;

            case "target":
                if (inlineValue is not null)
                    return ParseResult.Fail(10,
                        "/target takes no inline value; use sub-parameters: /target server:S database:D user:U password:P");
                state.ActiveEndpoint = new EndpointBuilder(isSource: false);
                i++;
                return null;

            // ── Sync switch ───────────────────────────────────────────────────
            case "sync":
                state.SyncMode = inlineValue is not null ? SyncMode.ScriptFile : SyncMode.Apply;
                state.SyncFilePath = inlineValue;
                i++;
                return null;

            // ── Control switches ──────────────────────────────────────────────
            case "q":
            case "quiet":
                state.Quiet = true;
                i++;
                return null;

            // ── File switches ─────────────────────────────────────────────────
            case "compfile":
                if (inlineValue is null)
                    return ParseResult.Fail(10, "/compfile requires a path: /compfile:<path>");
                state.CompFilePath = inlineValue;
                i++;
                return null;

            case "filter":
                if (inlineValue is null)
                    return ParseResult.Fail(10, "/filter requires a path: /filter:<path>");
                state.FilterFilePath = inlineValue;
                i++;
                return null;

            case "report":
                if (inlineValue is null)
                    return ParseResult.Fail(10, "/report requires a path: /report:<path>");
                state.ReportPath = inlineValue;
                i++;
                return null;

            case "log":
                if (inlineValue is null)
                    return ParseResult.Fail(10, "/log requires a path: /log:<path>");
                state.LogPath = inlineValue;
                i++;
                return null;

            case "reportformat":
                if (inlineValue is null)
                    return ParseResult.Fail(10, "/reportformat requires a value: /reportformat:HTML");
                if (inlineValue.Equals("XLS", StringComparison.OrdinalIgnoreCase))
                    return ParseResult.Fail(10,
                        "/reportformat:XLS is not implemented in v1. Use HTML, XML (schema), or CSV (data).");
                state.ReportFormat = inlineValue;
                i++;
                return null;

            case "password":
                // Standalone /password override (Devart documents this for connection-string commands).
                if (inlineValue is not null)
                    state.Options["_StandalonePassword"] = inlineValue;
                i++;
                return null;

            case "backup":
                return ParseResult.Fail(10,
                    "Operation /backup is not supported by ArtSync v1. " +
                    "If you meant a backup endpoint, use it as a /source or /target sub-parameter (which is also unsupported in v1).");

            // ── Warning-only display/report switches ──────────────────────────
            case "groupby":
            case "incsettings":
            case "scriptdiffsstyle":
                state.Warnings.Add(
                    $"/{switchName} is accepted but not fully implemented; output may differ from Devart.");
                if (inlineValue is not null)
                    state.Options[switchName] = inlineValue;
                i++;
                return null;

            // ── Argfile was already stripped in the outer loop ────────────────
            case "argfile":
                i++;
                return null;

            default:
                return HandleUnknownSwitch(switchName, switchLower, inlineValue, ref i, state);
        }
    }

    // ── Unknown-switch handler (option registry + unsupported operations) ─────

    private static ParseResult? HandleUnknownSwitch(
        string switchName, string switchLower, string? inlineValue, ref int i, ParseState state)
    {
        if (KnownOptions.IsUnsupportedOperation(switchLower))
        {
            var opHint = state.ImpliedOperation switch
            {
                OperationType.SchemaCompare => "schemacompare",
                OperationType.DataCompare => "datacompare",
                _ => "schemacompare or /datacompare",
            };
            return ParseResult.Fail(10,
                $"Operation /{switchName} is not implemented by ArtSync. " +
                $"See /{opHint} /? for supported operations.");
        }

        if (KnownOptions.IsWarningOnly(switchLower))
        {
            state.Warnings.Add(
                $"/{switchName} is accepted but not fully implemented; output may differ from Devart.");
            if (inlineValue is not null)
                state.Options[switchName] = inlineValue;
            i++;
            return null;
        }

        if (KnownOptions.TryGetCanonical(switchName, out var canonical))
        {
            if (KnownOptions.IsUnsupportedInV1(canonical))
            {
                return ParseResult.Fail(10,
                    $"Option /{switchName} ({canonical}) is not implemented in ArtSync v1. " +
                    $"Remove it from the command line.");
            }

            state.Options[canonical] = inlineValue ?? "yes";
            i++;
            return null;
        }

        return ParseResult.Fail(10, $"Unknown switch: /{switchName}");
    }

    // ── Result builder ────────────────────────────────────────────────────────

    private static ParseResult BuildResult(ParseState state, string argv0)
    {
        var op = state.Operation != OperationType.Unknown
            ? state.Operation
            : state.ImpliedOperation;

        if (op == OperationType.Unknown)
            return ParseResult.Fail(10,
                "No operation specified. Use /schemacompare or /datacompare (or rename the executable).");

        return ParseResult.Ok(new CommandRequest(
            Operation: op,
            Source: state.SourceBuilder?.Build(),
            Target: state.TargetBuilder?.Build(),
            SyncMode: state.SyncMode,
            SyncFilePath: state.SyncFilePath,
            ArgFilePath: state.ArgFilePath,
            CompFilePath: state.CompFilePath,
            FilterFilePath: state.FilterFilePath,
            ReportPath: state.ReportPath,
            LogPath: state.LogPath,
            ReportFormat: state.ReportFormat,
            Quiet: state.Quiet,
            Argv0: argv0,
            Options: new Dictionary<string, string>(state.Options, StringComparer.OrdinalIgnoreCase),
            Warnings: state.Warnings.AsReadOnly()
        ));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool TrySplitSwitch(string token, out string name, out string? value)
    {
        var body = token.TrimStart('/');
        var colon = body.IndexOf(':');
        if (colon < 0)
        {
            name = body;
            value = null;
        }
        else
        {
            name = body[..colon];
            value = body[(colon + 1)..];
        }
        return true;
    }

    private static bool IsSwitch(string token, string switchNameLower)
    {
        if (!token.StartsWith('/')) return false;
        TrySplitSwitch(token, out var n, out _);
        return n.Equals(switchNameLower, StringComparison.OrdinalIgnoreCase);
    }

    private static OperationType NormalizeArgv0(string argv0)
    {
        var name = Path.GetFileNameWithoutExtension(argv0).ToLowerInvariant();
        // Handle "schemacompare.com" → GetFileNameWithoutExtension → "schemacompare"
        // but also "dbforgesql.com" → "dbforgesql"
        return name switch
        {
            "schemacompare" => OperationType.SchemaCompare,
            "datacompare"   => OperationType.DataCompare,
            _               => OperationType.Unknown,
        };
    }

    // ── Inner types ───────────────────────────────────────────────────────────

    private sealed class ParseState
    {
        public OperationType ImpliedOperation;
        public OperationType Operation;
        public EndpointBuilder? SourceBuilder;
        public EndpointBuilder? TargetBuilder;
        public EndpointBuilder? ActiveEndpoint;
        public SyncMode SyncMode;
        public string? SyncFilePath;
        public string? ArgFilePath;
        public string? CompFilePath;
        public string? FilterFilePath;
        public string? ReportPath;
        public string? LogPath;
        public string? ReportFormat;
        public bool Quiet;
        public Dictionary<string, string> Options = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Warnings = [];

        public void EndCurrentEndpoint()
        {
            if (ActiveEndpoint is null) return;
            if (ActiveEndpoint.IsSource) SourceBuilder = ActiveEndpoint;
            else TargetBuilder = ActiveEndpoint;
            ActiveEndpoint = null;
        }
    }

    private sealed class EndpointBuilder(bool isSource)
    {
        public bool IsSource { get; } = isSource;

        private EndpointKind _kind;
        private string? _server, _database, _user, _password, _connStr;

        public ParseResult? AddSubParam(string token)
        {
            var colon = token.IndexOf(':');
            if (colon <= 0)
                return ParseResult.Fail(10,
                    $"Invalid endpoint sub-parameter '{token}'. Expected format: key:value " +
                    "(valid keys: connection, server, database, user, password).");

            var key = token[..colon].ToLowerInvariant();
            var value = token[(colon + 1)..];

            return key switch
            {
                "connection"    => SetConnStr(value),
                "server"        => SetSplit(ref _server, value),
                "database"      => SetSplit(ref _database, value),
                "user"          => SetSplit(ref _user, value),
                "password"      => SetSplit(ref _password, value),
                "backup"
                or "snapshot"
                or "scriptsfolder" =>
                    ParseResult.Fail(10,
                        $"Endpoint kind '{key}:' is not supported in ArtSync v1. " +
                        "Use 'connection:' or 'server:/database:/user:/password:' parameters."),
                _ => ParseResult.Fail(10,
                        $"Unknown endpoint sub-parameter '{key}'. " +
                        "Valid keys: connection, server, database, user, password.")
            };
        }

        public Endpoint Build() => _kind switch
        {
            EndpointKind.ConnectionString =>
                new Endpoint(EndpointKind.ConnectionString, ConnectionString: _connStr),
            _ =>
                new Endpoint(EndpointKind.LiveSplit,
                    Server: _server, Database: _database, User: _user, Password: _password),
        };

        private ParseResult? SetConnStr(string value)
        {
            if (_kind == EndpointKind.LiveSplit)
                return ParseResult.Fail(11,
                    "Cannot mix 'connection:' with 'server:/database:/user:/password:' parameters.");
            _kind = EndpointKind.ConnectionString;
            _connStr = value;
            return null;
        }

        private ParseResult? SetSplit(ref string? field, string value)
        {
            if (_kind == EndpointKind.ConnectionString)
                return ParseResult.Fail(11,
                    "Cannot mix 'server:/database:/user:/password:' with 'connection:' parameter.");
            _kind = EndpointKind.LiveSplit;
            field = value;
            return null;
        }
    }
}
