namespace ArtSync.Abstractions;

/// <summary>
/// Append-only operation log. Each call to <see cref="LogLine"/> writes a
/// timestamped line. Callers must dispose to flush/close the log file.
/// IO failures throw <see cref="LogIoException"/> (exit 106).
/// </summary>
public interface IOperationLogger : IDisposable
{
    void LogLine(string line);
}

/// <summary>
/// Writes an HTML, XML (schema), or CSV (data) comparison report to a file path.
/// IO failures throw <see cref="ReportIoException"/> (exit 107).
/// </summary>
public interface IResultReporter
{
    void WriteSchemaReport(
        string path,
        string format,
        SchemaCompareInfo info,
        CommandRequest request);

    void WriteDataReport(
        string path,
        string format,
        DataCompareInfo info,
        IReadOnlyList<RowDiff> diffs,
        CommandRequest request);
}

/// <summary>Thrown when the log file cannot be opened or written (exit 106).</summary>
public sealed class LogIoException : Exception
{
    public LogIoException(string message, Exception? inner = null)
        : base(message, inner) { }
}

/// <summary>Thrown when a report file cannot be written (exit 107).</summary>
public sealed class ReportIoException : Exception
{
    public ReportIoException(string message, Exception? inner = null)
        : base(message, inner) { }
}

/// <summary>No-op logger used when no <c>/log</c> path is specified.</summary>
public sealed class NullOperationLogger : IOperationLogger
{
    public static readonly NullOperationLogger Instance = new();
    private NullOperationLogger() { }
    public void LogLine(string line) { }
    public void Dispose() { }
}
