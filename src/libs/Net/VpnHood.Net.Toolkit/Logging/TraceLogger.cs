using Microsoft.Extensions.Logging;

namespace VpnHood.Net.Toolkit.Logging;

/// <summary>
/// Provides a logger that writes to <see cref="System.Diagnostics.Trace"/>, except on Linux.
/// </summary>
public sealed class TraceLogger : ILogger
{
    /// <inheritdoc/>
    /// <remarks>It keeps no scopes: it returns <see langword="null"/>.</remarks>
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return null;
    }

    /// <inheritdoc/>
    /// <remarks>It is <see langword="false"/> on Linux, otherwise <see langword="true"/>.</remarks>
    public bool IsEnabled(LogLevel logLevel)
    {
        return !OperatingSystem.IsLinux();
    }

    // Trace serializes its writers itself
    /// <inheritdoc/>
    /// <remarks>It writes the formatted message as a line through <c>Trace.WriteLine</c>, except on Linux.</remarks>
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (IsEnabled(logLevel))
            System.Diagnostics.Trace.WriteLine(formatter(state, exception));
    }
}
