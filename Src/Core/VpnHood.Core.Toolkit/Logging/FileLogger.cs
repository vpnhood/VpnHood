using System.Text;
using Microsoft.Extensions.Logging;

namespace VpnHood.Core.Toolkit.Logging;

public class FileLogger(string filePath, bool includeScopes = true, bool autoFlush = false, 
    string? categoryName = null)
    : TextLogger(includeScopes, categoryName), IDisposable
{
    private const int DefaultBufferSize = 1024;
    private readonly object _lock = new();
    private StreamWriter? _streamWriter = new(
        new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite), 
        Encoding.UTF8, DefaultBufferSize);

    public override void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var text = FormatLog(logLevel, eventId, state, exception, formatter);
        lock (_lock) {
            _streamWriter?.WriteLine(text);
            if (autoFlush || logLevel >= LogLevel.Error)
                _streamWriter?.Flush();
        }
    }

    public void Dispose()
    {
        lock (_lock) {
            if (_streamWriter == null)
                return;

            _streamWriter.Flush();
            _streamWriter.Dispose();
            _streamWriter = null;
        }
    }
}