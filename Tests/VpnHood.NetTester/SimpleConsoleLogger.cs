using Microsoft.Extensions.Logging;

namespace VpnHood.NetTester;

public class SimpleConsoleLogger : ILogger
{
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, 
        Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);

        if (!message.StartsWith("\n"))
            message = $"[{DateTime.Now:h:mm:ss}] {message}";

        Console.WriteLine(message);
    }

    public bool IsEnabled(LogLevel logLevel) => true;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
}