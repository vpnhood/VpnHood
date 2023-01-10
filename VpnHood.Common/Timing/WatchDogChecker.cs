using System;

namespace VpnHood.Common.Timing;

public class WatchDogChecker
{
    public bool ShouldEnter => Elapsed > Interval;
    public TimeSpan Elapsed => FastDateTime.Now - LastDoneTime;
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);
    public DateTime LastDoneTime { get; private set; }
    public bool AutoDone { get; set; } = true;

    public WatchDogChecker()
    {
    }

    public WatchDogChecker(TimeSpan interval)
    {
        Interval = interval;
    }

    public bool Enter()
    {
        lock (this)
        {
            if (!ShouldEnter)
                return false;

            if (AutoDone)
                LastDoneTime = FastDateTime.Now;

            return true;
        }
    }

    public void Done()
    {
        LastDoneTime = FastDateTime.Now;
    }
}