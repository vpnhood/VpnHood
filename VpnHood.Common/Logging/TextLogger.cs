using System;
using System.Text;
using Microsoft.Extensions.Logging;

namespace VpnHood.Common.Logging
{
    public abstract class TextLogger : ILogger, ILoggerProvider
    {
        private readonly bool _includeScopes;
        private readonly LoggerExternalScopeProvider _scopeProvider = new();

        protected TextLogger(bool includeScopes)
        {
            _includeScopes = includeScopes;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return _scopeProvider.Push(state);
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public abstract void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter);

        public ILogger CreateLogger(string categoryName)
        {
            return this;
        }

        public virtual void Dispose()
        {
        }

        protected void GetScopeInformation(StringBuilder stringBuilder)
        {
            var initialLength = stringBuilder.Length;
            _scopeProvider.ForEachScope((scope, state) =>
            {
                var (builder, length) = state;
                var first = length == builder.Length;
                builder.Append(first ? "=> " : " => ").Append(scope);
            }, (stringBuilder, initialLength));
        }

        protected string FormatLog<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var logBuilder = new StringBuilder();

            if (_includeScopes)
            {
                logBuilder.AppendLine();
                logBuilder.Append($"{logLevel.ToString().Substring(0, 4)} ");
                GetScopeInformation(logBuilder);
                logBuilder.AppendLine();
            }

            var message = "|" + eventId.Name + " | " + formatter(state, exception);
            logBuilder.Append(message);
            return logBuilder.ToString();
        }
    }
}