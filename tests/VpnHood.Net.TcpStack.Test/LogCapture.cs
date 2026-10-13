using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Net.TcpStack.Test;

/// <summary>
/// The process log's test sink: a test reads the lines whose exception carries its marker, so it sees what
/// its stack logged while other tests run beside it. Registered once, on first use.
/// </summary>
internal sealed class LogCapture : ILoggerProvider, ILogger
{
    public static LogCapture Instance { get; } = Create();
    private readonly List<(LogLevel Level, string Message, Exception? Exception)> _lines = [];

    private static LogCapture Create()
    {
        var capture = new LogCapture();
        VhLogger.AddProvider(capture);
        return capture;
    }

    public IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)> Lines(string marker)
    {
        lock (_lines)
            return _lines.Where(line => line.Exception?.Message.Contains(marker) == true).ToArray();
    }

    public ILogger CreateLogger(string categoryName) => this;
    public void Dispose() { }
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        lock (_lines)
            _lines.Add((logLevel, formatter(state, exception), exception));
    }
}
