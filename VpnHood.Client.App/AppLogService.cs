using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VpnHood.Client.App.Settings;
using VpnHood.Common.Logging;
using VpnHood.Tunneling;

namespace VpnHood.Client.App;

public class AppLogService
{
    private StreamLogger? _streamLogger;

    public AppLogService(string logFilePath)
    {
        LogFilePath = logFilePath;
        VhLogger.TcpCloseEventId = GeneralEventId.TcpLife;
    }

    public string LogFilePath { get; }

    public Task<string> GetLog()
    {
        return File.ReadAllTextAsync(LogFilePath);
    }

    public void Start(AppLogSettings logSettings, bool diagnose)
    {
        VhLogger.IsAnonymousMode = logSettings.LogAnonymous;
        VhLogger.IsDiagnoseMode = diagnose | logSettings.LogVerbose;
        VhLogger.Instance = NullLogger.Instance;
        VhLogger.Instance = CreateLogger(logSettings.LogToConsole, logSettings.LogToFile | diagnose, diagnose, true);
    }

    public void Stop()
    {
        VhLogger.Instance = NullLogger.Instance;
        VhLogger.Instance = CreateLogger(false, false, false, false);
        VhLogger.IsDiagnoseMode = false;
    }

    private ILogger CreateLogger(bool addToConsole, bool addToFile, bool verbose, bool removeLastFile)
    {
        var logger = CreateLoggerInternal(addToConsole, addToFile, verbose, removeLastFile);
        logger = new SyncLogger(logger);
        logger = new FilterLogger(logger, eventId =>
        {
            if (eventId == GeneralEventId.Session) return true;
            if (eventId == GeneralEventId.Tcp) return verbose;
            if (eventId == GeneralEventId.Ping) return verbose;
            if (eventId == GeneralEventId.Nat) return verbose;
            if (eventId == GeneralEventId.Dns) return verbose;
            if (eventId == GeneralEventId.Udp) return verbose;
            if (eventId == GeneralEventId.TcpLife) return verbose;
            if (eventId == GeneralEventId.Packet) return verbose;
            if (eventId == GeneralEventId.StreamProxyChannel) return verbose;
            if (eventId == GeneralEventId.DatagramChannel) return true;
            return true;
        });

        return logger;
    }

    private ILogger CreateLoggerInternal(bool addToConsole, bool addToFile, bool verbose, bool deleteLastFile)
    {
        // file logger, close old stream
        _streamLogger?.Dispose();
        _streamLogger = null;

        // delete last lgo
        if (deleteLastFile && File.Exists(LogFilePath))
            File.Delete(LogFilePath);

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            // console
            if (addToConsole)
            {
                builder.AddSimpleConsole(configure =>
                {
                    configure.TimestampFormat = "[HH:mm:ss.ffff] ";
                    configure.IncludeScopes = true;
                    configure.SingleLine = false;
                });
            }

            if (addToFile)
            {
                var fileStream = new FileStream(LogFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                _streamLogger = new StreamLogger(fileStream);
                builder.AddProvider(_streamLogger);
            }

            builder.SetMinimumLevel(verbose ? LogLevel.Trace : LogLevel.Information);
        });

        var logger = loggerFactory.CreateLogger("");
        return new SyncLogger(logger);
    }
}
