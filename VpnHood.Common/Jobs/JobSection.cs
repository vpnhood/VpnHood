using VpnHood.Common.Utils;

namespace VpnHood.Common.Jobs;

public class JobSection
{
    private readonly object _lockObject = new();
    private bool _runnerEntered;
    private bool _normalEntered;
    public TimeSpan Elapsed => FastDateTime.Now - LastDoneTime;
    public static TimeSpan DefaultInterval { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan Interval { get; set; } = DefaultInterval;
    public DateTime LastDoneTime { get; private set; } = FastDateTime.Now;
    public string? Name { get; init; }

    public JobSection()
    {
    }

    public JobSection(TimeSpan interval, string? name = null)
    {
        Interval = interval;
        Name = name;
    }

    public JobSection(JobOptions jobOptions)
        : this(jobOptions.Interval, jobOptions.Name)
    {
        if (jobOptions.DueTime != null)
            LastDoneTime = FastDateTime.Now - jobOptions.Interval + jobOptions.DueTime.Value;
    }

    public bool ShouldEnter(bool force = false)
    {
        return (force || Elapsed > Interval) && !_normalEntered;
    }

    private bool ShouldRunnerEnterInternal()
    {
        return Elapsed > Interval && !_normalEntered && !_runnerEntered;
    }

    /// <returns>true if job is done; false if skipped</returns>
    public async Task<bool> Enter(Func<Task> action, bool force = false)
    {
        // fast return;
        if (!ShouldEnter(force))
            return false;

        using var jobLock = Enter(force);
        if (!jobLock.IsEntered)
            return false;

        await action().VhConfigureAwait();
        return true;
    }

    public JobLock Enter(bool force = false)
    {
        lock (_lockObject)
        {
            if (!ShouldEnter(force))
                return new JobLock(this, false);

            _normalEntered = true;
            return new JobLock(this, true);
        }
    }

    internal bool ShouldRunnerEnter
    {
        get
        {
            lock (_lockObject)
                return ShouldRunnerEnterInternal();
        }
    }

    internal bool EnterRunner()
    {
        lock (_lockObject)
        {
            if (!ShouldRunnerEnterInternal())
                return false;

            _runnerEntered = true;
            return true;
        }
    }

    public void Leave()
    {
        lock (_lockObject)
        {
            LastDoneTime = FastDateTime.Now;
            _runnerEntered = false;
            _normalEntered = false;
        }
    }

    public void Reschedule()
    {
        lock (_lockObject)
            LastDoneTime = FastDateTime.Now;
    }
}