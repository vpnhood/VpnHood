using Microsoft.Extensions.Logging;
using VpnHood.Core.Tunneling;

namespace VpnHood.App.Server;

// A fallback log destination without the tracking and session lines: they carry raw client IPs, which
// only NLog.config's own files keep, for as long as the privacy policy says, and a fallback never rolls.
internal sealed class NoTrackingLogger(ILogger logger) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return logger.BeginScope(state);
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return logger.IsEnabled(logLevel);
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (eventId.Id == GeneralEventId.Track.Id || eventId.Id == GeneralEventId.SessionTrack.Id)
            return;

        logger.Log(logLevel, eventId, state, exception, formatter);
    }
}
