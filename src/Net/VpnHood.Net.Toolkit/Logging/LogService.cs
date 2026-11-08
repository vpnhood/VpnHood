using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace VpnHood.Net.Toolkit.Logging;

/// <summary>
/// Provides the process's log file and applies the user's log settings, for an app or for a VPN service in a process of
/// its own.
/// </summary>
/// <remarks>
/// The file joins the process's log at the first <see cref="Start"/> that opens it; each start, such as a connect's, opens
/// it again, starting afresh unless told to keep what it has, and <see cref="Stop"/> closes it. The settings go to
/// <see cref="VhLogger"/>'s level and redaction, which every sink is behind, before the file opens.
/// </remarks>
/// <param name="logFilePath">The log file's path.</param>
public class LogService(string logFilePath) : IDisposable
{
    private readonly FileLogger _file = new(logFilePath);
    private bool _isSinkAdded;
    private bool _disposed;

    /// <summary>Gets the log file's path.</summary>
    public string LogFilePath { get; } = logFilePath;

    /// <summary>Gets a value indicating whether the log file exists.</summary>
    public bool Exists => File.Exists(LogFilePath);

    /// <summary>
    /// Applies the options to the process's log, and opens the log file or closes it. With the options off, it does
    /// nothing.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <param name="deleteOldReport">
    /// <see langword="true"/> to start the file afresh; <see langword="false"/> to keep what it has, so a log opened
    /// before a connect is not the end of the last session's.
    /// </param>
    /// <exception cref="ObjectDisposedException">The service is disposed.</exception>
    /// <exception cref="IOException">The file cannot be opened.</exception>
    /// <exception cref="UnauthorizedAccessException">The process may not write the file.</exception>
    /// <exception cref="ArgumentException">The file's path is not valid.</exception>
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

        // the options as JSON only where the line is written: the serializer's first use, by
        // reflection, is a tenth of a second of a phone's start
        if (VhLogger.MinLogLevel <= LogLevel.Debug)
            VhLogger.Instance.LogDebug("LogService has started. Options: {Options}",
                JsonSerializer.Serialize(options));
    }

    /// <summary>Closes the log file; the log's level and redaction stay. Calling it on a closed file does nothing.</summary>
    public void Stop()
    {
        if (!_file.IsOpen)
            return;

        VhLogger.Instance.LogDebug("LogService is stopping...");
        _file.Close();
    }

    /// <summary>Joins event names with those a debug command names.</summary>
    /// <param name="currentNames">The names so far.</param>
    /// <param name="debugCommand">The debug command, whose <c>/log:</c> parts name events, separated by commas.</param>
    /// <returns>The names, each once, <c>*</c> among them.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="currentNames"/> is <see langword="null"/>.</exception>
    public static IEnumerable<string> GetLogEventNames(string[] currentNames, string debugCommand)
    {
        return currentNames
            .Concat(GetLogEventNames(debugCommand))
            .Distinct();
    }

    /// <summary>Gets the event names a debug command names.</summary>
    /// <param name="debugCommand">The debug command, whose <c>/log:</c> parts name events, separated by commas.</param>
    /// <returns>The names, each once, <c>*</c> first.</returns>
    public static IEnumerable<string> GetLogEventNames(string debugCommand)
    {
        var names = new List<string> { "*" };

        var parts = debugCommand.Split(' ').Where(x => x.Contains("/log:", StringComparison.OrdinalIgnoreCase));
        foreach (var part in parts)
            names.AddRange(part[5..].Split(','));

        // use user settings
        return names.Distinct();
    }

    /// <summary>Closes the log file, as <see cref="Stop"/> does. Calling it again does nothing.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, true))
            return;
        Stop();
    }
}
