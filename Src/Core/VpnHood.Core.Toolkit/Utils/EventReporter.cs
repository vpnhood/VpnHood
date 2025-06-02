using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Jobs;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.Core.Toolkit.Utils;

public class EventReporter : IDisposable
{
    private readonly object _lockObject = new();
    private readonly string _message;
    private readonly EventId _eventId;
    private readonly Job _reportJob;
    private bool _disposed;

    public int TotalEventCount { get; private set; }
    public int LastReportEventCount { get; private set; }
    public DateTime LastReportEventTime { get; private set; } = FastDateTime.Now;
    public LogScope LogScope { get; set; }


    public EventReporter(string message, EventId eventId = new(), LogScope? logScope = null, TimeSpan? period = null)
    {
        _message = message;
        _eventId = eventId;
        LogScope = logScope ?? new LogScope();
        _reportJob = new Job(ReportJob, period ?? JobOptions.DefaultPeriod, nameof(EventReporter));
    }

    public void Raise()
    {
        if (_disposed) 
            throw new ObjectDisposedException(GetType().Name);

        lock (_lockObject)
            TotalEventCount++;

        if (VhLogger.MinLogLevel <= LogLevel.Debug)
            ReportInternal();
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
            LastReportEventTime = FastDateTime.Now;
            LastReportEventCount = TotalEventCount;
        }
    }

    protected virtual void Report()
    {
        var args = new[] {
            Tuple.Create("EventDuration", (object?)(FastDateTime.Now - LastReportEventTime).ToString(@"hh\:mm\:ss")),
            Tuple.Create("EventCount", (object?)(TotalEventCount - LastReportEventCount)),
            Tuple.Create("EventTotal", (object?)TotalEventCount)
        };

        if (LogScope is { Data.Count: > 0 })
            args = args.Concat(LogScope.Data).ToArray();

        var log = _message + " " + string.Join(", ", args.Select(x => $"{x.Item1}: {{{x.Item1}}}"));
        VhLogger.Instance.LogInformation(_eventId, log, args.Select(x => x.Item2).ToArray());
    }

    public void Dispose()
    {
        if (_disposed) 
            return;

        ReportInternal();
        _reportJob.Dispose();
        _disposed = true;
    }
}