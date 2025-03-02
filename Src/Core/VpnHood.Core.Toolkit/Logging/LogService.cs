using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace VpnHood.Core.Toolkit.Logging;

public class LogService(string logFilePath) : IDisposable
{
    private StreamLogger? _streamLogger;
    public string LogFilePath { get; } = logFilePath;
    public string[] LogEvents { get; private set; } = [];
    public bool Exists => File.Exists(LogFilePath);
    public bool IsStarted => _streamLogger != null;
    public void Start(LogServiceOptions options)
    {
        Stop();

        VhLogger.IsAnonymousMode = options.LogAnonymous is null or true;
        VhLogger.IsDiagnoseMode = options.LogEventNames.Contains("*");
        VhLogger.Instance = CreateLogger(options);
        LogEvents = options.LogEventNames;
        if (options.LogLevel == LogLevel.Trace) {
            VhLogger.IsDiagnoseMode = true;
            if (!options.LogEventNames.Contains("*"))
                LogEvents = LogEvents.Concat(["*"]).ToArray();
        }
    }

    public void Stop()
    {
        VhLogger.Instance = NullLogger.Instance;
        _streamLogger?.Dispose();
        _streamLogger = null;
    }

    private ILogger CreateLogger(LogServiceOptions logServiceOptions)
    {
        var logger = CreateLoggerInternal(logServiceOptions);

        logger = new FilterLogger(logger, eventId => {
            if (logServiceOptions.LogEventNames.Contains(eventId.Name, StringComparer.OrdinalIgnoreCase))
                return true;

            return eventId.Id == 0 || logServiceOptions.LogEventNames.Contains("*");
        });

        logger = new SyncLogger(logger);
        return logger;
    }

    private ILogger CreateLoggerInternal(LogServiceOptions logServiceOptions)
    {
        // delete last lgo
        if (File.Exists(LogFilePath))
            File.Delete(LogFilePath);

        using var loggerFactory = LoggerFactory.Create(builder => {
            // console
            if (logServiceOptions.LogToConsole) // AddSimpleConsole does not support event id
                builder.AddProvider(new VhConsoleLogger(includeScopes: true, singleLine:
                    logServiceOptions.SingleLineConsole, globalScope: logServiceOptions.GlobalScope));

            if (logServiceOptions.LogToFile) {
                var fileStream = new FileStream(LogFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                _streamLogger = new StreamLogger(fileStream, autoFlush: logServiceOptions.AutoFlush, globalScope: logServiceOptions.GlobalScope);
                builder.AddProvider(_streamLogger);
            }

            builder.SetMinimumLevel(logServiceOptions.LogLevel);
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
        var names = new List<string> { "*" };

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

    // ReSharper disable once GrammarMistakeInComment
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