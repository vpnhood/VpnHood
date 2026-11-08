using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace VpnHood.Net.Toolkit.Logging;

/// <summary>
/// Provides a destination's loggers: a <see cref="TextLogger"/> per category over the destination's logger, so a
/// destination is a plain logger and a line of provider.
/// </summary>
/// <param name="createLogger">Makes the destination's logger for a category: the same one for every category, or one per category where the destination files by it.</param>
/// <param name="singleLine"><see langword="true"/> for one line per entry.</param>
/// <param name="includeTime"><see langword="true"/> to write the time.</param>
/// <param name="includeScopes"><see langword="true"/> to write the level and the scopes.</param>
public class TextLoggerProvider(
    Func<string, ILogger> createLogger,
    bool singleLine = true,
    bool includeTime = true,
    bool includeScopes = true)
    : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, TextLogger> _loggers = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="TextLoggerProvider"/> class over one logger for every category.
    /// </summary>
    /// <param name="logger">The destination's logger.</param>
    /// <param name="singleLine"><see langword="true"/> for one line per entry.</param>
    /// <param name="includeTime"><see langword="true"/> to write the time.</param>
    /// <param name="includeScopes"><see langword="true"/> to write the level and the scopes.</param>
    public TextLoggerProvider(ILogger logger, bool singleLine = true, bool includeTime = true, bool includeScopes = true)
        : this(_ => logger, singleLine, includeTime, includeScopes)
    {
    }

    /// <inheritdoc/>
    /// <remarks>It keeps one logger per category; under contention the destination's factory may run more than once.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="categoryName"/> is <see langword="null"/>.</exception>
    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName,
            name => new TextLogger(createLogger(name), singleLine, includeTime, includeScopes, name));
    }

    /// <summary>Forgets the loggers it made; the destination's loggers are not disposed.</summary>
    public void Dispose()
    {
        _loggers.Clear();
    }
}
