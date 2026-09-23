using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace VpnHood.Core.Toolkit.Logging;

// deviceLoggerProviderFactory: optional platform hook for the "device" log sink. When LogToDevice is
// enabled, this factory is invoked once per Start() (with the includeScopes flag) to create the device
// ILoggerProvider; if null, the default VhDeviceLoggerProvider (System.Diagnostics.Trace) is used. It is a
// factory (not an instance) because the LoggerFactory owns and disposes its providers each Start/Stop cycle
// — a fresh provider is needed per cycle. iOS passes a provider backed by os_log; other platforms pass nothing.
public class LogService(
    string logFilePath,
    Func<bool, ILoggerProvider>? deviceLoggerProviderFactory = null)
    : IDisposable
{
    private ILogger? _logger;
    private ILoggerFactory? _loggerFactory;
    private readonly List<ILoggerProvider> _loggerProviders = [];
    private bool _disposed;
    private readonly Lock _isStoppingLock = new();
    public string LogFilePath { get; } = logFilePath;
    public string[] LogEvents { get; private set; } = [];
    public bool Exists => File.Exists(LogFilePath);
    public bool IsStarted => _logger != null;

    public void Start(LogServiceOptions options, bool deleteOldReport = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Stop is a no-op unless a previous cycle actually started, so when this service is
        // disabled (or fresh) it does not reset the process-wide VhLogger here
        Stop();
        if (!options.Enabled)
            return;

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
            // not started (or already stopped): nothing to tear down, and VhLogger must be
            // left untouched — this service never installed a logger to restore from
            if (_logger == null)
                return;

            VhLogger.Instance.LogDebug("LogService is stopping...");

            VhLogger.Instance = VhLogger.CreateConsoleLogger();

            _loggerFactory?.Dispose();
            _loggerFactory = null;

            // LoggerFactory does NOT dispose providers that were added via builder.AddProvider(instance):
            // they are registered as externally-owned singletons (LoggerFactory flags injected providers
            // dispose:false, and the internal ServiceProvider never disposes an instance you registered
            // yourself). We must dispose them here or the FileLoggerProvider's FileStream leaks and keeps
            // the log file locked, making the next Start() -> File.Delete(LogFilePath) fail with a Windows
            // sharing violation ("used by another process").
            foreach (var provider in _loggerProviders)
                provider.Dispose();
            _loggerProviders.Clear();

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

    private ILoggerFactory CreateLoggerFactory(
        LogServiceOptions logServiceOptions, bool deleteOldReport, bool includeScopes = true)
    {
        // delete last log
        if (deleteOldReport && File.Exists(LogFilePath))
            File.Delete(LogFilePath);

        // Build the provider instances up-front and keep references to them (disposed in Stop()). They are
        // added via builder.AddProvider(instance) below, which registers them as externally-owned singletons
        // that neither the LoggerFactory nor its ServiceProvider will dispose — so LogService owns them.
        // device sink: platform-supplied provider (e.g. os_log on iOS) or the default

        if (logServiceOptions.LogToDevice)
            _loggerProviders.Add(deviceLoggerProviderFactory?.Invoke(includeScopes)
                                 ?? new VhDeviceLoggerProvider(includeScopes));

        // console
        if (logServiceOptions.LogToConsole) // AddSimpleConsole does not support event id
            _loggerProviders.Add(new VhConsoleLoggerProvider(includeScopes: true,
                singleLine: logServiceOptions.SingleLineConsole));

        if (logServiceOptions.LogToFile)
            _loggerProviders.Add(new FileLoggerProvider(LogFilePath, autoFlush: logServiceOptions.AutoFlush));

        var loggerFactory = LoggerFactory.Create(builder => {
            foreach (var provider in _loggerProviders)
                builder.AddProvider(provider);

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