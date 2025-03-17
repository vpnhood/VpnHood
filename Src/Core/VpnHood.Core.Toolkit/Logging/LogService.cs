using Microsoft.Extensions.Logging;

namespace VpnHood.Core.Toolkit.Logging;

public class LogService(string logFilePath) : IDisposable
{
    private ILogger? _logger;
    private readonly List<ILoggerProvider> _loggerProviders = [];
    public string LogFilePath { get; } = logFilePath;
    public string[] LogEvents { get; private set; } = [];
    public bool Exists => File.Exists(LogFilePath);
    public bool IsStarted => _logger != null;

    public void Start(LogServiceOptions options)
    {
        Stop();

        VhLogger.IsAnonymousMode = options.LogAnonymous is null or true;
        VhLogger.IsDiagnoseMode = options.LogEventNames.Contains("*");
        VhLogger.Instance = _logger = CreateLogger(options);
        LogEvents = options.LogEventNames;
        if (options.LogLevel == LogLevel.Trace) {
            VhLogger.IsDiagnoseMode = true;
            if (!options.LogEventNames.Contains("*"))
                LogEvents = LogEvents.Concat(["*"]).ToArray();
        }
    }

    public void Stop()
    {
        VhLogger.Instance = VhLogger.CreateConsoleLogger();
        foreach (var loggerProvider in _loggerProviders)
            loggerProvider.Dispose();
        _loggerProviders.Clear();
        _logger = null;
    }

    private ILogger CreateLogger(LogServiceOptions logServiceOptions)
    {
        using var loggerFactory = CreateLoggerFactory(logServiceOptions);
        var logger = loggerFactory.CreateLogger(logServiceOptions.CategoryName ?? "");

        logger = new FilterLogger(logger, eventId => {
            if (logServiceOptions.LogEventNames.Contains(eventId.Name, StringComparer.OrdinalIgnoreCase))
                return true;

            return eventId.Id == 0 || logServiceOptions.LogEventNames.Contains("*");
        });

        logger = new SyncLogger(logger);
        return logger;
    }

    private ILoggerFactory CreateLoggerFactory(LogServiceOptions logServiceOptions)
    {
        // delete last lgo
        if (File.Exists(LogFilePath))
            File.Delete(LogFilePath);

        var loggerFactory = LoggerFactory.Create(builder => {
            // console
            if (logServiceOptions.LogToConsole) // AddSimpleConsole does not support event id
            {
                var provider = new VhConsoleLoggerProvider(includeScopes: true,
                    singleLine: logServiceOptions.SingleLineConsole);
                _loggerProviders.Add(provider);
                builder.AddProvider(provider);
            }

            if (logServiceOptions.LogToFile) {
                var provider = new FileLoggerProvider(LogFilePath, autoFlush: logServiceOptions.AutoFlush);
                _loggerProviders.Add(provider);
                builder.AddProvider(provider);
            }

            builder.SetMinimumLevel(logServiceOptions.LogLevel);
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