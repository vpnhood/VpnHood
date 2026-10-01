using System.Text;
using Microsoft.Extensions.Logging;

namespace VpnHood.Net.Toolkit.Logging;

// A decorator that makes a line's text once for the logger behind it - the time, the category, the
// level and the scopes, the event's name, the message and the exception - and hands it over as the
// message. singleLine: one line per entry, its own line breaks written as "\n", for a destination
// that keeps each line as an entry of its own; includeTime: false where the destination stamps its
// lines itself.
public sealed class TextLogger(
    ILogger logger,
    bool singleLine = true,
    bool includeTime = true,
    bool includeScopes = true,
    string? categoryName = null)
    : ILogger
{
    private static readonly Func<string, Exception?, string> TextFormatter = (text, _) => text;
    private readonly LoggerExternalScopeProvider _scopeProvider = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return _scopeProvider.Push(state);
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return logger.IsEnabled(logLevel);
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!logger.IsEnabled(logLevel))
            return;

        logger.Log(logLevel, eventId, Format(logLevel, eventId, state, exception, formatter), null, TextFormatter);
    }

    private string Format<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var logBuilder = new StringBuilder();

        // time
        if (includeTime)
            logBuilder.Append($"{DateTime.Now:HH:mm:ss.ffff} | ");

        // category
        if (!string.IsNullOrEmpty(categoryName))
            logBuilder.Append($"{categoryName} | ");

        // level and scopes
        if (includeScopes) {
            logBuilder.Append(logLevel.ToString()[..4] + " |");
            var scopesStart = logBuilder.Length;
            WriteScopeInformation(logBuilder);
            if (!singleLine)
                logBuilder.AppendLine();
            else
                logBuilder.Append(logBuilder.Length == scopesStart ? " " : " | ");
        }

        // event
        if (!string.IsNullOrEmpty(eventId.Name)) {
            logBuilder.Append(eventId.Name);
            logBuilder.Append(" | ");
        }

        // message
        var message = formatter(state, exception);
        if (exception != null)
            message += "\r\nException: " + exception;

        logBuilder.Append(message);
        if (singleLine)
            return logBuilder.ToString().ReplaceLineEndings(@"\n");

        logBuilder.AppendLine();
        return logBuilder.ToString();
    }

    private void WriteScopeInformation(StringBuilder stringBuilder)
    {
        var initialLength = stringBuilder.Length;
        _scopeProvider.ForEachScope((scope, state) => {
            var (builder, length) = state;
            var first = length == builder.Length;
            builder.Append(first ? " " : " => ").Append(scope);
        }, (stringBuilder, initialLength));
    }
}
