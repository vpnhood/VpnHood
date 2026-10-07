using Microsoft.Extensions.Logging;

namespace VpnHood.AppLib.App.Linux;

// The console of a systemd service, whose stdout is the journal: a line starts with its level, "<3>"
// for an error, which the journal files it under and takes off. The journal stamps each line, so its
// provider asks TextLogger for no time.
public sealed class LinuxJournalLogger : ILogger
{
    // Whether stdout is the journal's stream. systemd names that stream in JOURNAL_STREAM,
    // "device:inode", and a process inherits the variable with an output of its own - a shell in a
    // terminal a desktop's service started, say - so the stream itself is compared, as systemd asks:
    // /proc names a socket "socket:[inode]".
    public static bool IsConsole { get; } = GetIsConsole();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Console.WriteLine($"<{GetPriority(logLevel)}>" + formatter(state, exception));
    }

    // syslog's priority for a level, which the journal files an entry under
    internal static int GetPriority(LogLevel logLevel)
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
