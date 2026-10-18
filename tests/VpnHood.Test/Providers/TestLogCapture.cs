using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Test.Providers;

// A sink of the process log that keeps the level of each line logged with an exception whose message CreateMessage
// made. The log can not drop a sink, so one serves the whole run, and a test finds its own lines by its message
public sealed class TestLogCapture : ILoggerProvider, ILogger
{
    private const string MessagePrefix = "Test log capture: ";
    private readonly ConcurrentQueue<(LogLevel LogLevel, string Message)> _entries = new();
    public static TestLogCapture Instance { get; } = Create();

    private static TestLogCapture Create()
    {
        var logCapture = new TestLogCapture();
        VhLogger.AddProvider(logCapture);
        return logCapture;
    }

    // an instance method, so the sink is in the log before a test logs with the message
    public string CreateMessage() => MessagePrefix + Guid.NewGuid();

    public IReadOnlyList<LogLevel> GetLevels(string message) =>
        _entries.Where(x => x.Message == message).Select(x => x.LogLevel).ToArray();

    public ILogger CreateLogger(string categoryName) => this;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (exception?.Message.StartsWith(MessagePrefix, StringComparison.Ordinal) == true)
            _entries.Enqueue((logLevel, exception.Message));
    }

    public void Dispose()
    {
    }
}
