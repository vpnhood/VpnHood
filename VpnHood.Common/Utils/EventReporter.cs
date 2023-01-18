using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VpnHood.Common.JobController;

namespace VpnHood.Common.Utils;

public class EventReporter : IDisposable, IJob
{
    private readonly object _lockObject = new();
    private readonly string _message;
    private readonly EventId _eventId;
    private readonly ILogger _logger;
    private bool _disposed;

    public JobSection JobSection { get; } = new(TimeSpan.FromSeconds(10));
    public int TotalEventCount { get; private set; }
    public int LastReportEventCount { get; private set; }
    public DateTime LastReportEventTime { get; private set; } = FastDateTime.Now;
    public List<Tuple<string, object?>> Data { get; set; } = new();
    public static bool IsDiagnosticMode { get; set; }

    public EventReporter(ILogger logger, string message, EventId eventId = new())
    {
        _logger = logger;
        _message = message;
        _eventId = eventId;

        JobRunner.Default.Add(this);
    }

    public void Raised()
    {
        if (_disposed) throw new ObjectDisposedException(GetType().Name);

        lock (_lockObject)
            TotalEventCount++;

        if (IsDiagnosticMode || TotalEventCount == 1 || FastDateTime.Now - LastReportEventTime > JobSection.Interval)
            ReportInternal();
    }

    private void ReportInternal()
    {
        lock (_lockObject)
        {
            //nothing to log
            if (TotalEventCount - LastReportEventCount == 0)
                return;

            Report();
            LastReportEventTime = FastDateTime.Now;
            LastReportEventCount = TotalEventCount;
            JobSection.Leave();
        }
    }

    protected virtual void Report()
    {
        var args = new[]
        {
            Tuple.Create("Duration", (object?)JobSection.Elapsed.ToString(@"hh\:mm\:ss")),
            Tuple.Create("Count", (object?)(TotalEventCount - LastReportEventCount)),
            Tuple.Create("Total", (object?)TotalEventCount)
        };

        if (Data.Count > 0)
            args = args.Concat(Data).ToArray();

        var log = _message + " " + string.Join(", ", args.Select(x => $"{x.Item1}: {{{x.Item1}}}"));
        _logger.LogInformation(_eventId, log, args.Select(x => x.Item2).ToArray());
    }

    public Task RunJob()
    {
        if (_disposed) throw new ObjectDisposedException(GetType().Name);

        ReportInternal();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        ReportInternal();
    }
}