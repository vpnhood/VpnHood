using Microsoft.Extensions.Logging;

namespace VpnHood.Net.Toolkit.Logging;

/// <summary>
/// Provides a logger that lets one thread at a time into the logger behind it. A failure of its <c>Log</c> is written to
/// the console instead of thrown; <c>BeginScope</c> and <c>IsEnabled</c> pass a failure on.
/// </summary>
/// <param name="logger">The logger behind it.</param>
public class SyncLogger(ILogger logger) : ILogger
{
    private readonly Lock _lock = new();

    /// <inheritdoc/>
    /// <remarks>It asks the logger behind it, under the lock.</remarks>
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        lock (_lock)
            return logger.BeginScope(state);
    }

    /// <inheritdoc/>
    /// <remarks>It asks the logger behind it, under the lock.</remarks>
    public bool IsEnabled(LogLevel logLevel)
    {
        lock (_lock)
            return logger.IsEnabled(logLevel);
    }

    /// <inheritdoc/>
    /// <remarks>It writes to the logger behind it, under the lock; a failure goes to the console.</remarks>
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        lock (_lock) {
            try {
                logger.Log(logLevel, eventId, state, exception, formatter);
            }
            catch (Exception ex) {
                Console.WriteLine($"Logger error! Could not write into logger. Error: {ex.Message}");
            }
        }
    }
}