using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace VpnHood.Net.Toolkit.Logging;

// The process's log file and the user's log settings - the app's, or a VPN service's in a process
// of its own. The file is the process's sink from the first Start on, opened at each start request
// (a connect) so it holds one session, and closed at Stop; the settings go to VhLogger's level and
// redaction, which every sink is behind. The other sinks are the head's, added at its start.
public class LogService(string logFilePath) : IDisposable
{
    private readonly FileLogger _file = new(logFilePath);
    private bool _isSinkAdded;
    private bool _disposed;

    public string LogFilePath { get; } = logFilePath;
    public bool Exists => File.Exists(LogFilePath);

    // deleteOldReport: false keeps what the file has, so a log opened before a connect is not the
    // end of the last session's
    public void Start(LogServiceOptions options, bool deleteOldReport = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!options.Enabled)
            return;

        VhLogger.IsAnonymousMode = options.LogAnonymous is null or true;
        VhLogger.MinLogLevel = options.MinLogLevel;

        if (options.LogToFile) {
            _file.AutoFlush = options.AutoFlush;
            _file.Open(deleteOld: deleteOldReport);
            if (!_isSinkAdded) {
                VhLogger.AddProvider(new TextLoggerProvider(_file, singleLine: false));
                _isSinkAdded = true;
            }
        }
        else {
            _file.Close();
        }

        VhLogger.Instance.LogDebug("LogService has started. Options: {Options}",
            JsonSerializer.Serialize(options));
    }

    public void Stop()
    {
        if (!_file.IsOpen)
            return;

        VhLogger.Instance.LogDebug("LogService is stopping...");
        _file.Close();
    }

    public static IEnumerable<string> GetLogEventNames(string[] currentNames, string debugCommand)
    {
        return currentNames
            .Concat(GetLogEventNames(debugCommand))
            .Distinct();
    }

    public static IEnumerable<string> GetLogEventNames(string debugCommand)
    {
        var names = new List<string> { "*" };

        var parts = debugCommand.Split(' ').Where(x => x.Contains("/log:", StringComparison.OrdinalIgnoreCase));
        foreach (var part in parts)
            names.AddRange(part[5..].Split(','));

        // use user settings
        return names.Distinct();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, true))
            return;
        Stop();
    }
}
