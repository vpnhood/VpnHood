using Microsoft.Extensions.Logging;

namespace VpnHood.App.StoreScreenshots;

// What the UI logs while it renders, kept so an error fails the shot: the pages report a failure
// through VhLogger before they show it, and a screenshot with an error behind it is not one to
// ship. The web UI engine reads the browser console for the same reason.
internal sealed class CapturingLogger : ILogger
{
    private readonly List<string> _errors = [];

    public IReadOnlyList<string> Errors {
        get {
            lock (_errors)
                return _errors.ToArray();
        }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        if (exception != null)
            message += $" ({exception.GetType().Name}: {exception.Message})";
        Console.WriteLine($"{logLevel,-11} {message}");

        if (logLevel >= LogLevel.Error)
            lock (_errors)
                _errors.Add(message);
    }
}
