using System;

namespace VpnHood.Common.Timing;

public class WatchDogResult : IDisposable
{
    private readonly WatchDogSection _watchDogSection;
    public bool ShouldEnter { get; }

    internal WatchDogResult(WatchDogSection watchDogSection, bool shouldEnter)
    {
        _watchDogSection = watchDogSection;
        ShouldEnter = shouldEnter;
    }

    public void Dispose()
    {
        if (ShouldEnter)
            _watchDogSection.Leave();
    }
}

public class WatchDogSection
{
    private readonly object _lockObject = new();
    private bool _runnerEntered;
    private bool _normalEntered;

    private bool ShouldEnter => Elapsed > Interval && !_normalEntered;
    private bool ShouldRunnerEnter => Elapsed > Interval && !_normalEntered && !_runnerEntered;

    public TimeSpan Elapsed => FastDateTime.Now - LastDoneTime;
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);
    public DateTime LastDoneTime { get; private set; }

    public WatchDogSection()
    {
    }

    public WatchDogSection(TimeSpan interval)
    {
        Interval = interval;
    }

    public WatchDogResult Enter()
    {
        lock (_lockObject)
        {
            if (!ShouldEnter)
                return new WatchDogResult(this, false);

            _normalEntered = true;
            return new WatchDogResult(this, true);
        }
    }

    internal bool RunnerEnter()
    {
        lock (_lockObject)
        {
            if (!ShouldRunnerEnter)
                return false;

            _runnerEntered = true;
            return true;
        }
    }

    public void Leave()
    {
        LastDoneTime = FastDateTime.Now;
        _runnerEntered = false;
        _normalEntered = false;
    }
}