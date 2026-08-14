using ArtSync.Abstractions;
using System.Text;

namespace ArtSync.Reporting;

/// <summary>
/// Appends timestamped lines to a UTF-8 text file.  Each <see cref="LogLine"/>
/// call is immediately flushed so the file is readable even if the process is
/// later killed.  IO failures throw <see cref="LogIoException"/> (exit 106).
/// Password values are redacted when an <see cref="ISecretRedactor"/> is supplied.
/// </summary>
public sealed class FileOperationLogger : IOperationLogger
{
    private readonly StreamWriter _writer;
    private readonly ISecretRedactor? _redactor;

    private FileOperationLogger(StreamWriter writer, ISecretRedactor? redactor)
    {
        _writer = writer;
        _redactor = redactor;
    }

    /// <summary>
    /// Opens (or creates) a log file at <paramref name="path"/> for append.
    /// Parent directories are created as needed.
    /// Throws <see cref="LogIoException"/> if the file cannot be opened.
    /// </summary>
    public static FileOperationLogger Open(string path, ISecretRedactor? redactor = null)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var sw = new StreamWriter(path, append: true, Encoding.UTF8) { AutoFlush = true };
            return new FileOperationLogger(sw, redactor);
        }
        catch (Exception ex) when (ex is not LogIoException)
        {
            throw new LogIoException($"Cannot open log file '{path}': {ex.Message}", ex);
        }
    }

    public void LogLine(string line)
    {
        try
        {
            var text = _redactor is null ? line : _redactor.Redact(line);
            _writer.WriteLine($"{DateTime.UtcNow:O}  {text}");
        }
        catch (Exception ex) { throw new LogIoException($"Log write failed: {ex.Message}", ex); }
    }

    public void Dispose() => _writer.Dispose();
}
