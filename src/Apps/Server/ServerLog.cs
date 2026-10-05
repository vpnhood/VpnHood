using Microsoft.Extensions.Logging;
using NLog;
using NLog.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.App.Server;

// The log of a server's start: NLog.config's console and files, loaded here at start and nowhere else,
// since its log file archives the one it finds when it opens, so a command run beside a running server
// would roll that server's log. A file that is missing, unreadable, broken or without rules leaves the
// terminal as the log, with the reason, rather than a server without a log.
internal sealed class ServerLog : IDisposable
{
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
        }
        catch (Exception ex) {
            VhLogger.AddProvider(new ConsoleLoggerProvider());
            VhLogger.Instance.LogError(ex,
                "Could not use the NLog configuration, so the log goes to the terminal only. ConfigFilePath: {ConfigFilePath}",
                configFilePath);
        }

        return new ServerLog();
    }

    // the last lines are written out
    public void Dispose()
    {
        LogManager.Shutdown();
    }
}
