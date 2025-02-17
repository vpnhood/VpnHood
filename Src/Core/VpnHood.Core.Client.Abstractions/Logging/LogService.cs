using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VpnHood.Core.Common.Logging;

namespace VpnHood.Core.Client.Abstractions.Logging;

public class LogService(string logFilePath) : IDisposable
{
    private StreamLogger? _streamLogger;
    public string LogFilePath { get; } = logFilePath;
    public string[] LogEvents { get; private set; } = [];
    public bool Exists => File.Exists(LogFilePath);
    public bool IsStarted => _streamLogger != null;
    public void Start(LogOptions logOptions)
    {
        VhLogger.IsAnonymousMode = logOptions.LogAnonymous is null or true;
        VhLogger.IsDiagnoseMode = logOptions.LogEventNames.Contains("*");
        VhLogger.Instance = CreateLogger(logOptions);
        LogEvents = logOptions.LogEventNames;
        if (logOptions.LogLevel == LogLevel.Trace) {
            VhLogger.IsDiagnoseMode = true;
            if (!logOptions.LogEventNames.Contains("*"))
                LogEvents = LogEvents.Concat(["*"]).ToArray();
        }
    }

    public void Stop()
    {
        _streamLogger?.Dispose();
        _streamLogger = null;
        VhLogger.Instance = NullLogger.Instance;
    }

    private ILogger CreateLogger(LogOptions logOptions)
    {
        var logger = CreateLoggerInternal(
            logToConsole: logOptions.LogToConsole,
            logToFile: logOptions.LogToFile,
            logLevel: logOptions.LogLevel,
            autoFlush: logOptions.AutoFlush,
            singleLineConsole: logOptions.SingleLineConsole);

        logger = new SyncLogger(logger);
        logger = new FilterLogger(logger, eventId => {
            if (logOptions.LogEventNames.Contains(eventId.Name, StringComparer.OrdinalIgnoreCase))
                return true;

            return eventId.Id == 0 || logOptions.LogEventNames.Contains("*");
        });

        return logger;
    }

    private ILogger CreateLoggerInternal(
        bool logToConsole, bool logToFile, LogLevel logLevel, bool autoFlush, bool singleLineConsole)
    {
        // delete last lgo
        if (File.Exists(LogFilePath))
            File.Delete(LogFilePath);

        using var loggerFactory = LoggerFactory.Create(builder => {
            // console
            if (logToConsole) // AddSimpleConsole does not support event id
                builder.AddProvider(new VhConsoleLogger(includeScopes: true, singleLine: singleLineConsole));

            if (logToFile) {
                var fileStream = new FileStream(LogFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                _streamLogger = new StreamLogger(fileStream, autoFlush: autoFlush);
                builder.AddProvider(_streamLogger);
            }

            builder.SetMinimumLevel(logLevel);
        });

        var logger = loggerFactory.CreateLogger("");
        return logger;
    }

    public static IEnumerable<string> GetLogEventNames(string[] currentNames, string debugCommand)
    {
        return currentNames
            .Concat(GetLogEventNames(debugCommand))
            .Distinct();
    }
    public static IEnumerable<string> GetLogEventNames(string debugCommand)
    {
        var names = new List<string> { "Session", "Essential" };

        var parts = debugCommand.Split(' ').Where(x => x.Contains("/log:", StringComparison.OrdinalIgnoreCase));
        foreach (var part in parts)
            names.AddRange(part[5..].Split(','));

        // use user settings
        return names.Distinct();
    }

    public void Dispose()
    {
        Stop();
    }

    //private class InterceptingLogger(ILogger innerLogger) : ILogger
    //{
    //    private static string UpdateMessage(string message)
    //    {
    //        if (!VpnHoodApp.IsInit)
    //            return message;

    //        var memInfo = VpnHoodApp.Instance.Device.MemInfo;
    //        if (memInfo is { TotalMemory: > 0 }) {
    //            var used = (memInfo.TotalMemory - memInfo.AvailableMemory) / 1_000_000;
    //            var total = memInfo.TotalMemory / 1_000_000;
    //            var mem = $"Mem: {used}/{total} ({used * 100 / total}%)";

    //            // find first linefeed and split into two parts
    //            message = message.Replace("\r\n", "\n");
    //            var index = message.Length > 2 ? message.TrimStart().IndexOf('\n') : 0;
    //            var part1 = index > 0 ? message[..index] : message;
    //            var part2 = index > 0 ? message[(index + 1)..] : "";

    //            // append memory info
    //            part1 = part1.TrimEnd().TrimEnd('|').TrimEnd();
    //            message = part1 + " | " + mem;
    //            if (part2.Length > 0)
    //                message += "\n" + part2;

    //            message = message.Replace("\n", Environment.NewLine);
    //        }

    //        return message;
    //    }

    //    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
    //        Func<TState, Exception?, string> formatter) =>
    //        innerLogger.Log(logLevel, eventId, state, exception, (_, _) => UpdateMessage(formatter(state, exception)));

    //    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => innerLogger.BeginScope(state);
    //    public bool IsEnabled(LogLevel logLevel) => innerLogger.IsEnabled(logLevel);
    //}
}