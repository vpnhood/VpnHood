using System.Text;
using Microsoft.Extensions.Logging;

namespace VpnHood.Net.Toolkit.Logging;

/// <summary>
/// Provides a logger that makes a line's text once, for the logger behind it: the time in UTC, the category, the level and
/// the scopes, the event's name, the message and the exception; it hands that over as the message.
/// </summary>
/// <remarks>
/// A line reads <c>HH:mm:ss.ffff | category | Info | scope =&gt; scope | event | message</c>: the time in UTC, the level's first
/// four characters, the scopes separated by <c>=&gt;</c>, the event's name without its number, and after the message
/// <c>Exception:</c> and the exception's text. A single line has every line break written as <c>\n</c>; otherwise the
/// entry ends with a line break.
/// </remarks>
/// <param name="logger">The logger behind it.</param>
/// <param name="singleLine">
/// <see langword="true"/> for one line per entry, its own line breaks written as <c>\n</c>, for a destination that keeps
/// each line as an entry of its own.
/// </param>
/// <param name="includeTime"><see langword="true"/> to write the time; <see langword="false"/> where the destination stamps its lines itself.</param>
/// <param name="includeScopes"><see langword="true"/> to write the scopes.</param>
/// <param name="categoryName">The category to write, or <see langword="null"/> for none.</param>
/// <param name="includeLevel">
/// <see langword="true"/> to write the level; <see langword="false"/> where the destination records it itself.
/// </param>
public sealed class TextLogger(
    ILogger logger,
    bool singleLine = true,
    bool includeTime = true,
    bool includeScopes = true,
    string? categoryName = null,
    bool includeLevel = true)
    : ILogger
{
    private static readonly Func<string, Exception?, string> TextFormatter = (text, _) => text;
    private readonly LoggerExternalScopeProvider _scopeProvider = new();

    /// <inheritdoc/>
    /// <remarks>The scopes are its own, written into each line with <c>includeScopes</c>.</remarks>
    public IDisposable BeginScope<TState>(TState state) where TState : notnull
    {
        return _scopeProvider.Push(state);
    }

    /// <inheritdoc/>
    /// <remarks>It asks the logger behind it.</remarks>
    public bool IsEnabled(LogLevel logLevel)
    {
        return logger.IsEnabled(logLevel);
    }

    /// <inheritdoc/>
    /// <remarks>It writes the line's text to the logger behind it, when that one takes the level.</remarks>
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

        // time, in UTC: a process's first read of local time loads its time zone, slow on some phones
        if (includeTime)
            logBuilder.Append($"{DateTime.UtcNow:HH:mm:ss.ffff} | ");

        // category
        if (!string.IsNullOrEmpty(categoryName))
            logBuilder.Append($"{categoryName} | ");

        // level and scopes, each by its own switch
        if (includeLevel || includeScopes) {
            if (includeLevel)
                logBuilder.Append(logLevel.ToString()[..4] + " |");

            var scopesStart = logBuilder.Length;
            if (includeScopes)
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
