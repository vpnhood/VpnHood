using Microsoft.Extensions.Logging;

namespace VpnHood.Loggers
{
    public class SyncLogger : ILogger
    {
        private readonly ILogger _logger;
        private readonly object _lock = new object();

        public SyncLogger(ILogger logger) => _logger = logger;

        public System.IDisposable BeginScope<TState>(TState state)
        {
            lock(_lock)
                return _logger.BeginScope(state);
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return _logger.IsEnabled(logLevel);
        }

        public void Log<TState>(LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, System.Exception exception, System.Func<TState, System.Exception, string> formatter)
        {
            lock(_lock)
                _logger.Log(logLevel, eventId, state, exception, formatter);
        }
    }

}
