using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Settings;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Tunneling;

namespace VpnHood.AppLib;

public class AppLogService : IDisposable
{
    private readonly bool _singleLineConsole;
    private ILoggerProvider? _streamLogger;
    public string LogFilePath { get; }
    public string[] LogEvents { get; private set; } = [];
    public bool Exists => File.Exists(LogFilePath);

    public AppLogService(string logFilePath, bool singleLineConsole)
    {
        _singleLineConsole = singleLineConsole;
        LogFilePath = logFilePath;
        VhLogger.TcpCloseEventId = GeneralEventId.TcpLife;
    }

    public async Task<string> GetLog()
    {
        // read text file use shared access none
        await using var stream = new FileStream(LogFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    public void Start(AppLogOptions logOptions)
    {
        VhLogger.IsAnonymousMode = logOptions.LogAnonymous;
        VhLogger.IsDiagnoseMode = logOptions.LogEventNames.Contains("*");
        VhLogger.Instance = CreateLogger(logOptions, removeLastFile: true);
        LogEvents = logOptions.LogEventNames;
    }

    public void Stop()
    {
        _streamLogger?.Dispose();
        LogEvents = [];
    }

    private ILogger CreateLogger(AppLogOptions logOptions, bool removeLastFile)
    {
        var logger = CreateLoggerInternal(
            logToConsole: logOptions.LogToConsole,
            logToFile: logOptions.LogToFile,
            logLevel: logOptions.LogLevel,
            removeLastFile: removeLastFile,
            autoFlush: logOptions.AutoFlush);

        logger = new SyncLogger(logger);
        logger = new FilterLogger(logger, eventId => {
            if (logOptions.LogEventNames.Contains(eventId.Name, StringComparer.OrdinalIgnoreCase))
                return true;

            return eventId.Id == 0 || logOptions.LogEventNames.Contains("*");
        });

        return logger;
    }

    private ILogger CreateLoggerInternal(bool logToConsole, bool logToFile, LogLevel logLevel,
        bool removeLastFile, bool autoFlush)
    {
        // file logger, close old stream
        _streamLogger?.Dispose();

        // delete last lgo
        if (removeLastFile && File.Exists(LogFilePath))
            File.Delete(LogFilePath);

        using var loggerFactory = LoggerFactory.Create(builder => {
            // console
            if (logToConsole) // AddSimpleConsole does not support event id
                builder.AddProvider(new VhConsoleLogger(includeScopes: true, singleLine: _singleLineConsole));

            if (logToFile) {
                var fileStream = new FileStream(LogFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                _streamLogger = new StreamLogger(fileStream, autoFlush: autoFlush);
                builder.AddProvider(_streamLogger);
            }

            builder.SetMinimumLevel(logLevel);
        });

        var logger = loggerFactory.CreateLogger("");
        var interceptingLogger = new InterceptingLogger(logger);
        return new SyncLogger(interceptingLogger);
    }

    public static string[] GetLogEventNames(bool verbose, bool diagnose, string? debugCommand)
    {
        debugCommand ??= "";

        // log verbose
        if (verbose || debugCommand.Contains("/verbose", StringComparison.OrdinalIgnoreCase))
            return ["*"];

        // Extract all event names from debugData that contains "log:EventName1,EventName2"
        var names = new List<string?> {
            GeneralEventId.Session.Name,
            GeneralEventId.Essential.Name
        };

        if (diagnose)
            names.AddRange([
                GeneralEventId.Essential.Name,
                GeneralEventId.Nat.Name,
                GeneralEventId.Ping.Name,
                GeneralEventId.Dns.Name,
                GeneralEventId.Tcp.Name,
                GeneralEventId.Tls.Name,
                GeneralEventId.StreamProxyChannel.Name,
                GeneralEventId.DatagramChannel.Name,
                GeneralEventId.Request.Name,
                GeneralEventId.TcpLife.Name,
                GeneralEventId.Test.Name,
                GeneralEventId.UdpSign.Name,
            ]);

        var parts = debugCommand.Split(' ').Where(x => x.Contains("/log:", StringComparison.OrdinalIgnoreCase));
        foreach (var part in parts)
            names.AddRange(part[5..].Split(','));

        // use user settings
        return names.Distinct().ToArray()!;
    }

    public void Dispose()
    {
        Stop();
    }

    private class InterceptingLogger(ILogger innerLogger) : ILogger
    {
        private static string UpdateMessage(string message)
        {
            if (!VpnHoodApp.IsInit)
                return message;

            var memInfo = VpnHoodApp.Instance.Device.MemInfo;
            if (memInfo is { TotalMemory: > 0 }) {
                var used = (memInfo.TotalMemory - memInfo.AvailableMemory) / 1_000_000;
                var total = memInfo.TotalMemory / 1_000_000;
                var mem = $"Mem: {used}/{total} ({used * 100 / total}%)";

                // find first linefeed and split into two parts
                message = message.Replace("\r\n", "\n");
                var index = message.IndexOf('\n', 4);
                var part1 = index > 0 ? message[..index] : message;
                var part2 = index > 0 ? message[(index + 1)..] : "";

                // append memory info
                part1 = part1.TrimEnd().TrimEnd('|').TrimEnd();
                message = part1 + " | " + mem;
                if (part2.Length > 0)
                    message += "\n" + part2;

                message = message.Replace("\n", Environment.NewLine);
            }

            return message;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            innerLogger.Log(logLevel, eventId, state, exception, (_, _) => UpdateMessage(formatter(state, exception)));
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => innerLogger.BeginScope(state);
        public bool IsEnabled(LogLevel logLevel) => innerLogger.IsEnabled(logLevel);
    }
}


