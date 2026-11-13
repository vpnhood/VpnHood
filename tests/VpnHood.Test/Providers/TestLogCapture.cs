using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Test.Providers;

// A sink of the process log that keeps the level of each line logged with an exception whose message CreateMessage
// made, and the text of each warning. The log can not drop a sink, so one serves the whole run, and a test finds its
// own lines by its message
public sealed class TestLogCapture : ILoggerProvider, ILogger
{
    private const string MessagePrefix = "Test log capture: ";
    private readonly ConcurrentQueue<(LogLevel LogLevel, string Message)> _entries = new();
    private readonly ConcurrentQueue<string> _warnings = new();
    private readonly ConcurrentQueue<string> _eventLines = new();
    public static TestLogCapture Instance { get; } = Create();

    // the name of the event whose lines are kept as they are written, for a test that runs alone
    public const string CapturedEventName = "TestLogCapture";

    // set, a Debug line with this text fails, as a sink that cannot write does; for a test that runs alone
    public volatile string? FailingDebugMessage;

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

    public IReadOnlyList<string> GetWarnings() => _warnings.ToArray();

    public IReadOnlyList<string> GetEventLines() => _eventLines.ToArray();

    public ILogger CreateLogger(string categoryName) => this;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (exception?.Message.StartsWith(MessagePrefix, StringComparison.Ordinal) == true)
            _entries.Enqueue((logLevel, exception.Message));

        if (logLevel == LogLevel.Warning)
            _warnings.Enqueue(formatter(state, exception));

        if (eventId.Name == CapturedEventName)
            _eventLines.Enqueue(formatter(state, exception));

        if (logLevel == LogLevel.Debug && FailingDebugMessage is { } failingMessage &&
            formatter(state, exception) == failingMessage)
            throw new InvalidOperationException("Test: the sink could not write.");
    }

    public void Dispose()
    {
    }
}
