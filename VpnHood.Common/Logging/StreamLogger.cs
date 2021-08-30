using System;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace VpnHood.Logging
{
    public class StreamLogger : TextLogger
    {
        private const int _defaultBufferSize = 1024;
        private readonly StreamWriter _streanWriter;

        public StreamLogger(Stream stream, bool includeScopes = true, bool leaveOpen = false)
            : base(includeScopes)
        {
            _streanWriter = new StreamWriter(stream, Encoding.UTF8, _defaultBufferSize, leaveOpen);
        }

        public override void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception,
            Func<TState, Exception, string> formatter)
        {
            var text = FormatLog(logLevel, eventId, state, exception, formatter);
            _streanWriter.WriteLine(text);
        }

        public override void Dispose()
        {
            _streanWriter.Dispose();
            base.Dispose();
        }
    }
}