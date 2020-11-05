using Microsoft.Extensions.Logging;

namespace VpnHood.AccessServer.Test
{
    public class TestUtil
    {
        public static ILogger CreateConsoleLogger(bool verbose = false)
        {
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole((config) => { config.IncludeScopes = true; });
                builder.SetMinimumLevel(verbose ? LogLevel.Trace : LogLevel.Information);
            });
            var logger = loggerFactory.CreateLogger("");
            return logger;
        }
    }
}
