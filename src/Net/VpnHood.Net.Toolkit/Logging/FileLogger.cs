using System.Text;
using Microsoft.Extensions.Logging;

namespace VpnHood.Net.Toolkit.Logging;

// The process's log file: opened at each start request (Open), in place of what it had or after
// it, and closed at stop; closed, it takes nothing. Its provider asks TextLogger for entries that
// keep their lines.
public sealed class FileLogger(string filePath) : ILogger
{
    private const int DefaultBufferSize = 1024;
    private readonly Lock _lock = new();
    private StreamWriter? _writer;

    public string FilePath { get; } = filePath;
    public bool IsOpen => _writer != null;

    // every line as it comes when true; buffered otherwise, and flushed at an error either way
    public bool AutoFlush { get; set; }

    public void Open(bool deleteOld)
    {
        lock (_lock) {
            _writer?.Dispose();
            _writer = null;
            if (deleteOld && File.Exists(FilePath))
                File.Delete(FilePath);

            _writer = new StreamWriter(
                new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.Read),
                Encoding.UTF8, DefaultBufferSize);
        }
    }

    public void Close()
    {
        lock (_lock) {
            _writer?.Dispose();
            _writer = null;
        }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return IsOpen;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        lock (_lock) {
            if (_writer == null)
                return;

            try {
                _writer.WriteLine(formatter(state, exception));
                if (AutoFlush || logLevel >= LogLevel.Error)
                    _writer.Flush();
            }
            catch (Exception ex) {
                Console.WriteLine($"Error: Could not write the log. {ex.Message}");
            }
        }
    }
}
