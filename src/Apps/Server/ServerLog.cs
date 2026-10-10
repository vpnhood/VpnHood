using Microsoft.Extensions.Logging;
using NLog;
using NLog.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.App.Server;

// The log of a server's start: NLog.config's console and files, loaded here at start and nowhere else,
// since its log file archives the one it finds when it opens, so a command run beside a running server
// would roll that server's log. A file that is missing, unreadable, broken or without rules leaves the
// terminal and the shipped config's own log file as the log, with the reason, rather than a server
// without a log: an installed unit sends the terminal nowhere. That log has no tracking or session
// lines (NoTrackingLogger).
internal sealed class ServerLog : IDisposable
{
    private readonly FileLogger? _fallbackFileLogger;

    private ServerLog(FileLogger? fallbackFileLogger)
    {
        _fallbackFileLogger = fallbackFileLogger;
    }

    public static ServerLog Start(string storagePath, string appFolderPath)
    {
        var configFilePath = Path.Combine(storagePath, "NLog.config");
        if (!File.Exists(configFilePath)) configFilePath = Path.Combine(appFolderPath, "NLog.config");

        try {
            LogManager.ThrowConfigExceptions = true;
            LogManager.Setup().LoadConfigurationFromFile(configFilePath, optional: false);
            var configuration = LogManager.Configuration ??
                                throw new NLogConfigurationException("The configuration is empty.");
            if (configuration.LoggingRules.Count == 0)
                throw new NLogConfigurationException("The configuration has no logging rules.");

            configuration.Variables["mydir"] = storagePath;
            VhLogger.AddProvider(new NLogLoggerProvider());
            VhLogger.Instance.LogInformation("Logger has been created. ConfigFilePath: {configFilePath}, LogLevel: {LogLevel}",
                configFilePath, VhLogger.MinLogLevel);
            return new ServerLog(fallbackFileLogger: null);
        }
        catch (Exception ex) {
            VhLogger.AddProvider(new TextLoggerProvider(new NoTrackingLogger(new ConsoleLogger())));
            var logFolderPath = Path.Combine(storagePath, "logs");
            var fileLogger = new FileLogger(Path.Combine(logFolderPath, "server.log"));
            // the file never rolls, so each start begins it anew, inside the privacy policy's 30 days; a
            // broken config is rare enough to lose the run before it
            VhUtils.TryInvoke("Open the fallback log file", () => {
                Directory.CreateDirectory(logFolderPath);
                fileLogger.Open(deleteOld: true);
            });

            if (fileLogger.IsOpen)
                VhLogger.AddProvider(new TextLoggerProvider(new NoTrackingLogger(fileLogger), singleLine: false));

            VhLogger.Instance.LogError(ex,
                "Could not use the NLog configuration, so the log goes to {LogDestination}. ConfigFilePath: {ConfigFilePath}",
                fileLogger.IsOpen ? $"the terminal and {fileLogger.FilePath}" : "the terminal only", configFilePath);
            return new ServerLog(fileLogger.IsOpen ? fileLogger : null);
        }
    }

    // the last lines are written out, whichever log took them
    public void Dispose()
    {
        LogManager.Shutdown();
        _fallbackFileLogger?.Close();
    }
}
