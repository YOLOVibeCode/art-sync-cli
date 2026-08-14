namespace ArtSync.Abstractions;

public enum OperationType
{
    Unknown = 0,
    SchemaCompare,
    DataCompare,
    Activate,
    Deactivate,
    Help,
    ExitCodes,
    Unsupported,
}

public enum SyncMode
{
    None,
    Apply,
    ScriptFile,
}

public enum EndpointKind
{
    Unset = 0,
    LiveSplit,
    ConnectionString,
    UnsupportedKind,
}

/// <summary>A resolved source or target endpoint.</summary>
public record Endpoint(
    EndpointKind Kind,
    string? Server = null,
    string? Database = null,
    string? User = null,
    string? Password = null,
    string? ConnectionString = null,
    string? UnsupportedKindName = null
);

/// <summary>Fully parsed command ready for an operation handler.</summary>
public record CommandRequest(
    OperationType Operation,
    Endpoint? Source,
    Endpoint? Target,
    SyncMode SyncMode,
    string? SyncFilePath,
    string? ArgFilePath,
    string? CompFilePath,
    string? FilterFilePath,
    string? ReportPath,
    string? LogPath,
    string? ReportFormat,
    bool Quiet,
    string Argv0,
    IReadOnlyDictionary<string, string> Options,
    IReadOnlyList<string> Warnings
);

/// <summary>Discriminated result from the argv parser: either a usable request or an exit code + message.</summary>
public abstract record ParseResult
{
    public sealed record Success(CommandRequest Request) : ParseResult;
    public sealed record Failure(int ExitCode, string Message) : ParseResult;

    private ParseResult() { }

    // Static factory helpers so callers write ParseResult.Ok(...) / ParseResult.Fail(...)
    // without scattering `new` throughout the parser.
    public static ParseResult Ok(CommandRequest request) => new Success(request);
    public static ParseResult Fail(int code, string message) => new Failure(code, message);
}

/// <summary>Result from an operation handler.</summary>
public record OperationResult(int ExitCode, string? Message = null);
