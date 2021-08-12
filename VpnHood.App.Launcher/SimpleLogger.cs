using System;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;

namespace VpnHood.App.Launcher
{
    class SimpleLogger : ILogger
    {
        readonly LoggerExternalScopeProvider _scopeProvider = new ();

        public IDisposable BeginScope<TState>(TState state) => _scopeProvider.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            var message = formatter(state, exception);
            Console.WriteLine(message);
        }
    }
}
