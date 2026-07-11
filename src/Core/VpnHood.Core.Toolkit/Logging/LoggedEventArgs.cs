using Microsoft.Extensions.Logging;

namespace VpnHood.Core.Toolkit.Logging;

public class LoggedEventArgs(LogLevel logLevel, EventId eventId, string message, Exception? exception) : EventArgs
{
    public LogLevel LogLevel { get; } = logLevel;
    public EventId EventId { get; } = eventId;
    public string Message { get; } = message;
    public Exception? Exception { get; } = exception;
}
