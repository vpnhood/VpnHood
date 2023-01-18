using System;
using VpnHood.Common.Utils;

namespace VpnHood.Common.JobController;

public class JobSection
{
    private readonly object _lockObject = new();
    private bool _runnerEntered;
    private bool _normalEntered;

    private bool ShouldEnter => Elapsed > Interval && !_normalEntered;
    private bool ShouldRunnerEnter => Elapsed > Interval && !_normalEntered && !_runnerEntered;

    public TimeSpan Elapsed => FastDateTime.Now - LastDoneTime;
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);
    public DateTime LastDoneTime { get; private set; } = FastDateTime.Now;

    public JobSection()
    {
    }

    public JobSection(TimeSpan interval)
    {
        Interval = interval;
    }

    public JobLock Enter()
    {
        lock (_lockObject)
        {
            if (!ShouldEnter)
                return new JobLock(this, false);

            _normalEntered = true;
            return new JobLock(this, true);
        }
    }

    internal bool EnterRunner()
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