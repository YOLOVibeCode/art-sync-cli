namespace ArtSync.Abstractions;

/// <summary>
/// Parses a Devart-style argv array into a <see cref="CommandRequest"/>.
/// Internally resolves /argfile by delegating to <see cref="IArgFileLoader"/>.
/// Callers do NOT pass the executable name in argv; pass it separately as argv0.
/// </summary>
public interface IArgvParser
{
    ParseResult Parse(IReadOnlyList<string> argv, string argv0);
}

/// <summary>
/// Loads a Devart argfile and returns the contained tokens in the same
/// format as an OS-split argv (quotes already stripped, one logical
/// argument per element).
/// </summary>
public interface IArgFileLoader
{
    IReadOnlyList<string> Load(string path);
}

/// <summary>
/// Replaces password values in connection strings and argv arrays before
/// they are written to logs or reports. The original objects are never
/// mutated; redacted copies are returned.
/// </summary>
public interface ISecretRedactor
{
    /// <summary>Returns a copy of <paramref name="input"/> with password values replaced by ***.</summary>
    string Redact(string input);

    /// <summary>Returns a new argv list with password values in each element replaced by ***.</summary>
    IReadOnlyList<string> RedactArgv(IReadOnlyList<string> argv);
}

/// <summary>
/// Executes one compare/sync operation. Each concrete handler implements
/// exactly one <see cref="OperationType"/>. Callers depend only on this
/// single-method interface; they never import engine-specific types.
/// </summary>
public interface IOperationHandler
{
    OperationResult Run(CommandRequest request);
}
