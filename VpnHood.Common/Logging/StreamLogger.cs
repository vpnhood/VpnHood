using System;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace VpnHood.Common.Logging
{
    public class StreamLogger : TextLogger
    {
        private const int DefaultBufferSize = 1024;
        private readonly StreamWriter _streamWriter;

        public StreamLogger(Stream stream, bool includeScopes = true, bool leaveOpen = false)
            : base(includeScopes)
        {
            _streamWriter = new StreamWriter(stream, Encoding.UTF8, DefaultBufferSize, leaveOpen);
        }

        public override void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var text = FormatLog(logLevel, eventId, state, exception, formatter);
            _streamWriter.WriteLine(text);
        }

        public override void Dispose()
        {
            _streamWriter.Dispose();
            base.Dispose();
        }
    }
}