using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Jobs;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Net.Toolkit.Utils;

/// <summary>
/// Provides a counter of a repeated event that logs a summary at an interval instead of a line per event: the message,
/// the events since the last report, their span and the total.
/// </summary>
/// <remarks>
/// With the log at Debug or lower, every event reports at once. A report goes at Information, with the scope's data.
/// </remarks>
public class EventReporter : IDisposable
{
    private readonly Lock _lockObject = new();
    private readonly string _message;
    private readonly EventId _eventId;
    private readonly Job _reportJob;
    private bool _disposed;

    /// <summary>Gets the number of events since the reporter was made.</summary>
    public int TotalEventCount { get; private set; }

    /// <summary>Gets <see cref="TotalEventCount"/> as of the last report.</summary>
    public int LastReportEventCount { get; private set; }

    /// <summary>Gets when the last report was made, in UTC; when the reporter was made, until then.</summary>
    public DateTime LastReportEventTime { get; private set; } = FastDateTime.UtcNow;

    /// <summary>Gets or sets the scope whose data each report carries.</summary>
    public LogScope LogScope { get; set; }

    /// <summary>Initializes a new instance of the <see cref="EventReporter"/> class, which starts its report job.</summary>
    /// <param name="message">The message each report starts with.</param>
    /// <param name="eventId">The reports' event ID.</param>
    /// <param name="logScope">The scope whose data each report carries, or <see langword="null"/> for none.</param>
    /// <param name="period">
    /// How often to report, or <see langword="null"/> for <see cref="Jobs.JobOptions.DefaultInterval"/>.
    /// </param>
    public EventReporter(string message, EventId eventId = new(), LogScope? logScope = null, TimeSpan? period = null)
    {
        _message = message;
        _eventId = eventId;
        LogScope = logScope ?? new LogScope();
        _reportJob = new Job(ReportJob, period ?? JobOptions.DefaultInterval, nameof(EventReporter));
    }

    /// <summary>Counts an event.</summary>
    /// <returns>
    /// <see langword="true"/> for the first event since the last report, so a caller can log its details beside the
    /// counts; otherwise, <see langword="false"/>.
    /// </returns>
    /// <exception cref="ObjectDisposedException">The reporter is disposed.</exception>
    public bool Raise()
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);

        var isFirstSinceReport = CountEvent();
        if (VhLogger.MinLogLevel <= LogLevel.Debug)
            ReportInternal();

        return isFirstSinceReport;
    }

    private bool CountEvent()
    {
        lock (_lockObject) {
            TotalEventCount++;
            return TotalEventCount - LastReportEventCount == 1;
        }
    }

    private ValueTask ReportJob(CancellationToken cancellationToken)
    {
        ReportInternal();
        return default;
    }

    private void ReportInternal()
    {
        lock (_lockObject) {
            //nothing to log
            if (TotalEventCount - LastReportEventCount == 0)
                return;

            Report();
            LastReportEventTime = FastDateTime.UtcNow;
            LastReportEventCount = TotalEventCount;
        }
    }

    /// <summary>Writes a report; called under the reporter's lock, only when there are events to report.</summary>
    protected virtual void Report()
    {
        var args = new[] {
            Tuple.Create("EventDuration", (object?)(FastDateTime.UtcNow - LastReportEventTime).ToString(@"hh\:mm\:ss")),
            Tuple.Create("EventCount", (object?)(TotalEventCount - LastReportEventCount)),
            Tuple.Create("EventTotal", (object?)TotalEventCount)
        };

        if (LogScope is { Data.Count: > 0 })
            args = args.Concat(LogScope.Data).ToArray();

        var log = _message + " " + string.Join(", ", args.Select(x => $"{x.Item1}: {{{x.Item1}}}"));
        VhLogger.Instance.LogInformation(_eventId, log, args.Select(x => x.Item2).ToArray());
    }

    /// <summary>Reports what is left and stops the report job. Calling it again does nothing.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        ReportInternal();
        _reportJob.Dispose();
    }
}