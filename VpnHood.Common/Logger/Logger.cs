using Microsoft.Extensions.Logging;
using System;

namespace VpnHood.Logger
{
    public static class Logger
    {
        private static Lazy<ILogger> _logger = new Lazy<ILogger>(() => CreateConsoleLogger());
        public static ILogger Current { get => _logger.Value; set => _logger = new Lazy<ILogger>(value); }
        public static ILogger CreateConsoleLogger(bool verbose = false)
        {
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole((config) => { config.IncludeScopes = true; });
                builder.SetMinimumLevel(verbose ? LogLevel.Trace : LogLevel.Information);
            });
            var logger = loggerFactory.CreateLogger("");
            return new SyncLogger(logger);
        }
    }
}
