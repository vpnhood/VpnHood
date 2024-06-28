using Microsoft.Extensions.Logging;

namespace VpnHood.Common.Logging;

public class VhConsoleLogger(bool includeScopes = true) : TextLogger(includeScopes)
{
    public override void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var text = FormatLog(logLevel, eventId, state, exception, formatter);
        Console.WriteLine(text);
    }
}