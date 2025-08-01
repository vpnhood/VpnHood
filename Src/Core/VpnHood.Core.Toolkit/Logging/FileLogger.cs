using System.Text;
using Microsoft.Extensions.Logging;

namespace VpnHood.Core.Toolkit.Logging;

public class FileLogger(
    string filePath,
    bool includeScopes = true,
    bool autoFlush = false,
    string? categoryName = null)
    : TextLogger(includeScopes, categoryName), IDisposable
{
    private const int DefaultBufferSize = 1024;
    private readonly object _lock = new();
    private readonly StreamWriter _streamWriter = new(
        new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read),
        Encoding.UTF8, DefaultBufferSize);

    public override void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var text = FormatLog(logLevel, eventId, state, exception, formatter);
        lock (_lock) {
            try {
                _streamWriter.WriteLine(text);
                if (autoFlush || logLevel >= LogLevel.Error)
                    _streamWriter.Flush();
            }
            catch (Exception ex) {
                Console.WriteLine($"Error: Could not write the log. {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
            _streamWriter.Dispose(); //it will handle flush and close the stream
    }
}