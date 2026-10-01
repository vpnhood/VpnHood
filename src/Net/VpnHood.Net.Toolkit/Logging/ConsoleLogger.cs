using Microsoft.Extensions.Logging;

namespace VpnHood.Net.Toolkit.Logging;

// A terminal: the message as a line, in the level's color where the console has colors.
public sealed class ConsoleLogger : ILogger
{
    private static readonly bool IsColorSupported = GetIsColorSupported();
    private readonly Lock _lock = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

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
