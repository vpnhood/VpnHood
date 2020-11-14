using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using System;

namespace VpnHood.Loggers
{
    public static class Logger 
    {
        private static Lazy<ILogger> _logger = new Lazy<ILogger>(() => CreateConsoleLogger());
        public static ILogger Current { get => _logger.Value; set =>_logger = new Lazy<ILogger>(value);}
        public static ILogger CreateConsoleLogger(bool verbose = false)
        {
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddSimpleConsole((configure)=> { configure.IncludeScopes = true; configure.SingleLine = false;});
                builder.SetMinimumLevel(verbose ? LogLevel.Trace : LogLevel.Information);

        });
            var logger = loggerFactory.CreateLogger("");
            return new SyncLogger(logger);
        }
    }
}
