using System.Text;
using Microsoft.Extensions.Logging;

namespace VpnHood.Core.Common.Logging;

public class StreamLogger(Stream stream, bool includeScopes = true, bool leaveOpen = false, bool autoFlush = false)
    : TextLogger(includeScopes)
{
    private const int DefaultBufferSize = 1024;
    private StreamWriter? _streamWriter = new(stream, Encoding.UTF8, DefaultBufferSize, leaveOpen);
    private readonly object _lock = new();

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

    public override void Dispose()
    {
        lock (_lock) {
            if (_streamWriter == null)
                return;

            _streamWriter.Flush();
            _streamWriter.Dispose();
            _streamWriter = null;
            base.Dispose();
        }
    }
}