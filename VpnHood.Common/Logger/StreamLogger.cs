using System;
using Microsoft.Extensions.Logging;
using System.Text;
using System.IO;

namespace VpnHood.Logger
{
    public class StreamLogger : TextLogger
    {
        private readonly StreamWriter _streanWriter;
        private const int _defaultBufferSize = 1024;

        public StreamLogger(Stream stream, bool includeScopes = true, bool leaveOpen = false)
            : base(includeScopes)
        {
            _streanWriter = new StreamWriter(stream, Encoding.UTF8, _defaultBufferSize, leaveOpen);
        }

        public override void Log<TState>(LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
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
