using Microsoft.Extensions.Logging;

namespace VpnHood.Net.Toolkit.Logging;

/// <summary>
/// Provides a logger that writes each message as a line to the console, in the level's color where the console has
/// colors.
/// </summary>
public sealed class ConsoleLogger : ILogger
{
    private static readonly bool IsColorSupported = GetIsColorSupported();
    private readonly Lock _lock = new();

    /// <inheritdoc/>
    /// <remarks>It keeps no scopes: it returns <see langword="null"/>.</remarks>
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return null;
    }

    /// <inheritdoc/>
    /// <remarks>It is always <see langword="true"/>.</remarks>
    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>It writes the formatted message as a line, one writer at a time.</remarks>
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var text = formatter(state, exception);
        lock (_lock) {
            if (!IsColorSupported) {
                Console.WriteLine(text);
                return;
            }

            var prevColor = Console.ForegroundColor;
            Console.ForegroundColor = GetColor(logLevel);
            Console.WriteLine(text);
            Console.ForegroundColor = prevColor;
        }
    }

    private static ConsoleColor GetColor(LogLevel logLevel)
    {
        return logLevel switch {
            LogLevel.Trace or LogLevel.Debug => ConsoleColor.Gray,
            LogLevel.Information => ConsoleColor.White,
            LogLevel.Warning => ConsoleColor.Yellow,
            LogLevel.Error => ConsoleColor.Red,
            LogLevel.Critical => ConsoleColor.DarkRed,
            _ => ConsoleColor.White
        };
    }

    // a console without colors says so by throwing, each platform in its own way
    private static bool GetIsColorSupported()
    {
        try {
            _ = Console.ForegroundColor;
            return true;
        }
        catch {
            return false;
        }
    }
}
