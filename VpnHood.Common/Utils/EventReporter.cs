﻿using System;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Timing;

namespace VpnHood.Common.Utils;

public class EventReporter : IDisposable, IWatchDog
{
    private readonly object _lockObject = new();
    private readonly string _message;
    private readonly EventId _eventId;
    private readonly ILogger _logger;
    private bool _disposed;

    public WatchDogChecker WatchDogChecker { get; } = new();
    public int TotalEventCount { get; private set; }
    public int LastReportEventCount { get; private set; }
    public static bool IsDiagnosticMode { get; set; }

    public EventReporter(ILogger logger, string message, EventId eventId = new())
    {
        _logger = logger;
        _message = message;
        _eventId = eventId;

        WatchDogRunner.Default.Add(this);
    }

    public void Raised()
    {
        if (_disposed) throw new ObjectDisposedException(GetType().Name);

        lock (_lockObject)
            TotalEventCount++;

        if (TotalEventCount == 1 || IsDiagnosticMode || WatchDogChecker.ShouldEnter)
            ReportInternal();
    }

    private void ReportInternal()
    {
        lock (_lockObject)
        {
            //nothing to log
            if (TotalEventCount - LastReportEventCount > 0)
                Report();

            LastReportEventCount = TotalEventCount;
            WatchDogChecker.Done();
        }
    }

    protected virtual void Report()
    {
        _logger.LogInformation(_eventId, _message + ". Duration: {Duration}, Count: {Count}, TotalCount: {Total}",
            TotalEventCount - LastReportEventCount, WatchDogChecker.Elapsed, TotalEventCount);
    }

    public void DoWatch()
    {
        if (_disposed) throw new ObjectDisposedException(GetType().Name);
        ReportInternal();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        ReportInternal();
    }
}