using Microsoft.Extensions.Logging;

namespace VpnHood.NetTester.Utils;

public class SimpleLogger(string? file = null) : ILogger
{
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);

        if (exception!= null)
            message += " " + exception.Message;

        if (!message.StartsWith("\n"))
            message = $"[{DateTime.Now:h:mm:ss}] {message}";

        Console.WriteLine(message);
        if (!string.IsNullOrEmpty(file))
            File.AppendAllText(file, message + Environment.NewLine);

    }

    public bool IsEnabled(LogLevel logLevel) => true;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
}