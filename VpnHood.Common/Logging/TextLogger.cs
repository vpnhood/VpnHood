using System.Text;
using Microsoft.Extensions.Logging;

namespace VpnHood.Common.Logging;

public abstract class TextLogger(bool includeScopes) : ILogger, ILoggerProvider
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

    public ILogger CreateLogger(string categoryName)
    {
        return this;
    }

    public virtual void Dispose()
    {
    }

    protected void GetScopeInformation(StringBuilder stringBuilder)
    {
        var initialLength = stringBuilder.Length;
        _scopeProvider.ForEachScope((scope, state) =>
        {
            var (builder, length) = state;
            var first = length == builder.Length;
            builder.Append(first ? "" : " => ").Append(scope);
        }, (stringBuilder, initialLength));
    }

    protected string FormatLog<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var logBuilder = new StringBuilder();
        var time = DateTime.Now.ToString("HH:mm:ss.ffff");

        if (includeScopes)
        {
            logBuilder.AppendLine();
            logBuilder.Append($"{time} | ");
            logBuilder.Append(logLevel.ToString()[..4] + " | ");
            GetScopeInformation(logBuilder);
            logBuilder.AppendLine();
        }
        else
            logBuilder.Append($"{time} | ");

        // event
        if (!string.IsNullOrEmpty(eventId.Name))
        {
            logBuilder.Append(eventId.Name);
            logBuilder.Append(" | ");
        }

        // message
        var message = formatter(state, exception);
        if (exception != null)
            message += "\r\nException: " + exception;

        logBuilder.Append(message);
        return logBuilder.ToString();
    }
}