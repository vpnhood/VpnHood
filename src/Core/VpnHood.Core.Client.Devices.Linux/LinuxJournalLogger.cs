using Microsoft.Extensions.Logging;

namespace VpnHood.Core.Client.Devices.Linux;

/// <summary>
/// Represents the console of a systemd service, whose stdout is the journal: each line starts with its level, as
/// <c>&lt;3&gt;</c> for an error, which the journal files it under and takes off.
/// </summary>
/// <remarks>The journal stamps each line, so its provider asks <c>TextLogger</c> for no time.</remarks>
public sealed class LinuxJournalLogger : ILogger
{
    /// <summary>
    /// Gets a value indicating whether stdout is the journal's stream.
    /// </summary>
    /// <remarks>
    /// systemd names that stream in <c>JOURNAL_STREAM</c>, as <c>device:inode</c>, and a process inherits the variable
    /// with an output of its own — a shell in a terminal a desktop's service started, say — so the stream itself is
    /// compared, as systemd asks: <c>/proc</c> names a socket <c>socket:[inode]</c>.
    /// </remarks>
    public static bool IsConsole { get; } = GetIsConsole();

    /// <summary>Begins no scope: the journal's lines have none.</summary>
    /// <typeparam name="TState">The type of the scope's state.</typeparam>
    /// <param name="state">The scope's state.</param>
    /// <returns><see langword="null"/>.</returns>
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return null;
    }

    /// <summary>Determines whether a level is written: every level is, as its provider filters.</summary>
    /// <param name="logLevel">The level.</param>
    /// <returns><see langword="true"/>.</returns>
    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    /// <summary>Writes an entry to stdout as a line that starts with its syslog priority.</summary>
    /// <typeparam name="TState">The type of the entry's state.</typeparam>
    /// <param name="logLevel">The entry's level.</param>
    /// <param name="eventId">The entry's event, which the line leaves out.</param>
    /// <param name="state">The entry's state.</param>
    /// <param name="exception">The entry's exception, or <see langword="null"/>.</param>
    /// <param name="formatter">The function that makes the entry's text.</param>
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Console.WriteLine($"<{GetPriority(logLevel)}>" + formatter(state, exception));
    }

    /// <summary>Gets syslog's priority for a level, which the journal files an entry under.</summary>
    /// <param name="logLevel">The level.</param>
    /// <returns>2 for critical, 3 for error, 4 for warning, 6 for information, and 7 for the rest.</returns>
    public static int GetPriority(LogLevel logLevel)
    {
        return logLevel switch {
            LogLevel.Critical => 2,
            LogLevel.Error => 3,
            LogLevel.Warning => 4,
            LogLevel.Information => 6,
            _ => 7
        };
    }

    private static bool GetIsConsole()
    {
        if (!OperatingSystem.IsLinux() ||
            Environment.GetEnvironmentVariable("JOURNAL_STREAM") is not { } journalStream)
            return false;

        var inode = journalStream[(journalStream.IndexOf(':') + 1)..];
        try {
            return new FileInfo("/proc/self/fd/1").LinkTarget == $"socket:[{inode}]";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return false;
        }
    }
}
