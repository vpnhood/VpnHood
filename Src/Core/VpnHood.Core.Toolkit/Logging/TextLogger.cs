using System.Text;
using Microsoft.Extensions.Logging;

namespace VpnHood.Core.Toolkit.Logging;

public abstract class TextLogger(bool includeScopes, string? categoryName) : ILogger
{
    private readonly LoggerExternalScopeProvider _scopeProvider = new();

    public IDisposable BeginScope<TState>(TState state) where TState : notnull
    {
        return _scopeProvider.Push(state);
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public abstract void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter);

    protected void WriteScopeInformation(StringBuilder stringBuilder)
    {

        var initialLength = stringBuilder.Length;
        _scopeProvider.ForEachScope((scope, state) => {
            var (builder, length) = state;
            var first = length == builder.Length;
            builder.Append(first ? " " : " => ").Append(scope);
        }, (stringBuilder, initialLength));
    }

    protected virtual string FormatLog<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var time = DateTime.Now.ToString("HH:mm:ss.ffff");
        var logBuilder = new StringBuilder();

        // time
        logBuilder.Append($"{time} | ");

        // category
        if (!string.IsNullOrEmpty(categoryName))
            logBuilder.Append($"{categoryName} | ");

        // scopes
        if (includeScopes) {
            logBuilder.Append(logLevel.ToString()[..4] + " |");
            WriteScopeInformation(logBuilder);
            logBuilder.AppendLine();
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
        logBuilder.AppendLine();
        return logBuilder.ToString();
    }
 }