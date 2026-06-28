using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace VpnHood.Core.Toolkit.Logging;

// deviceLoggerProviderFactory: optional platform hook for the "device" log sink. When LogToDevice is
// enabled, this factory is invoked once per Start() to create the device ILoggerProvider; if null, the
// default VhDeviceLoggerProvider (System.Diagnostics.Trace) is used. It is a factory (not an instance)
// because the LoggerFactory owns and disposes its providers each Start/Stop cycle — a fresh provider is
// needed per cycle. iOS passes a provider backed by os_log; other platforms pass nothing.
public class LogService(
    string logFilePath, 
    Func<ILoggerProvider>? deviceLoggerProviderFactory = null)
    : IDisposable
{
    private ILogger? _logger;
    private ILoggerFactory? _loggerFactory;
    private bool _disposed;
    private readonly Lock _isStoppingLock = new();
    public string LogFilePath { get; } = logFilePath;
    public string[] LogEvents { get; private set; } = [];
    public bool Exists => File.Exists(LogFilePath);
    public bool IsStarted => _logger != null;

    public void Start(LogServiceOptions options, bool deleteOldReport = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Stop();

        VhLogger.IsAnonymousMode = options.LogAnonymous is null or true;
        VhLogger.Instance = _logger = CreateLogger(options, deleteOldReport);
        LogEvents = options.LogEventNames;
        VhLogger.MinLogLevel = options.MinLogLevel;
        if (options.MinLogLevel == LogLevel.Trace) {
            if (!options.LogEventNames.Contains("*"))
                LogEvents = LogEvents.Concat(["*"]).ToArray();
        }

        // report logger to VhLogger
        VhLogger.Instance.LogDebug("LogService has started. Options: {Options}",
            JsonSerializer.Serialize(options));
    }


    public void Stop()
    {
        lock (_isStoppingLock) {
            VhLogger.Instance.LogDebug("LogService is stopping...");

            VhLogger.Instance = VhLogger.CreateConsoleLogger();

            // The factory owns its providers; disposing it disposes them once, at the end of the
            // Start/Stop cycle (not while the logger is still in use).
            _loggerFactory?.Dispose();
            _loggerFactory = null;
            _logger = null;
        }
    }

    private ILogger CreateLogger(LogServiceOptions logServiceOptions, bool deleteOldReport)
    {
        // Keep the factory for the lifetime of this Start/Stop cycle; it is disposed in Stop().
        _loggerFactory = CreateLoggerFactory(logServiceOptions, deleteOldReport);
        var logger = _loggerFactory.CreateLogger(logServiceOptions.CategoryName ?? "");

        logger = new FilterLogger(logger, (_, eventId) => {
            if (logServiceOptions.LogEventNames.Contains(eventId.Name, StringComparer.OrdinalIgnoreCase))
                return true;

            return eventId.Id == 0 || logServiceOptions.LogEventNames.Contains("*");
        });

        return logger;
    }

    private ILoggerFactory CreateLoggerFactory(LogServiceOptions logServiceOptions, bool deleteOldReport)
    {
        // delete last lgo
        if (deleteOldReport && File.Exists(LogFilePath))
            File.Delete(LogFilePath);

        // The returned LoggerFactory owns every provider added below and disposes them when it is
        // disposed (in Stop()); no separate provider tracking is needed.
        var loggerFactory = LoggerFactory.Create(builder => {
            // device sink: platform-supplied provider (e.g. os_log on iOS) or the default
            // VhDeviceLoggerProvider (System.Diagnostics.Trace).
            if (logServiceOptions.LogToDevice)
                builder.AddProvider(deviceLoggerProviderFactory?.Invoke()
                                    ?? new VhDeviceLoggerProvider(includeScopes: true));

            // console
            if (logServiceOptions.LogToConsole) // AddSimpleConsole does not support event id
                builder.AddProvider(new VhConsoleLoggerProvider(includeScopes: true,
                    singleLine: logServiceOptions.SingleLineConsole));

            if (logServiceOptions.LogToFile)
                builder.AddProvider(new FileLoggerProvider(LogFilePath, autoFlush: logServiceOptions.AutoFlush));

            builder.SetMinimumLevel(logServiceOptions.MinLogLevel);
        });

        return loggerFactory;
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