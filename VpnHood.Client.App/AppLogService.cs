using Microsoft.Extensions.Logging;
using VpnHood.Client.App.Settings;
using VpnHood.Common.Logging;
using VpnHood.Tunneling;

namespace VpnHood.Client.App;

public class AppLogService : IDisposable
{
    private readonly bool _singleLineConsole;
    private StreamLogger? _streamLogger;
    public string LogFilePath { get; }
    public string[] LogEvents { get; private set; } = [];

    public AppLogService(string logFilePath, bool singleLineConsole)
    {
        _singleLineConsole = singleLineConsole;
        LogFilePath = logFilePath;
        VhLogger.TcpCloseEventId = GeneralEventId.TcpLife;
    }

    public Task<string> GetLog()
    {
        return File.ReadAllTextAsync(LogFilePath);
    }

    public void Start(AppLogSettings logSettings)
    {
        VhLogger.IsAnonymousMode = logSettings.LogAnonymous;
        VhLogger.IsDiagnoseMode = logSettings.LogEventNames.Contains("*");
        VhLogger.Instance = CreateLogger(logSettings, removeLastFile: true);
        LogEvents = logSettings.LogEventNames;
    }

    public void Stop()
    {
        _streamLogger?.Dispose();
        LogEvents = [];
    }

    private ILogger CreateLogger(AppLogSettings logSettings, bool removeLastFile)
    {
        var logger = CreateLoggerInternal(
            logToConsole: logSettings.LogToConsole,
            logToFile: logSettings.LogToFile,
            logLevel: logSettings.LogLevel,
            removeLastFile: removeLastFile);

        logger = new SyncLogger(logger);
        logger = new FilterLogger(logger, eventId =>
        {
            if (logSettings.LogEventNames.Contains(eventId.Name, StringComparer.OrdinalIgnoreCase))
                return true;

            return eventId.Id == 0 || logSettings.LogEventNames.Contains("*");
        });

        return logger;
    }

    private ILogger CreateLoggerInternal(bool logToConsole, bool logToFile, LogLevel logLevel, bool removeLastFile)
    {
        // file logger, close old stream
        _streamLogger?.Dispose();

        // delete last lgo
        if (removeLastFile && File.Exists(LogFilePath))
            File.Delete(LogFilePath);

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            // console
            if (logToConsole) // AddSimpleConsole does not support event id
                builder.AddProvider(new VhConsoleLogger(includeScopes: true, singleLine: _singleLineConsole));

            if (logToFile)
            {
                var fileStream = new FileStream(LogFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                _streamLogger = new StreamLogger(fileStream);
                builder.AddProvider(_streamLogger);
            }

            builder.SetMinimumLevel(logLevel);
        });

        var logger = loggerFactory.CreateLogger("");
        return new SyncLogger(logger);
    }

    public static string[] GetLogEventNames(bool verbose, string? debugCommand, string[] defaults)
    {
        if (verbose) return ["*"];
        debugCommand ??= "";
        if (!defaults.Any()) defaults = [GeneralEventId.Session.Name!];

        // Extract all event names from debugData that contains "log:EventName1,EventName2
        var names = new List<string>();
        var parts = debugCommand.Split(' ').Where(x => x.Contains("/log:", StringComparison.OrdinalIgnoreCase));
        foreach (var part in parts)
            names.AddRange(part[5..].Split(','));

        // use user settings
        return names.Count > 0 ? names.ToArray() : defaults;
    }

    public void Dispose()
    {
        Stop();
    }
}
