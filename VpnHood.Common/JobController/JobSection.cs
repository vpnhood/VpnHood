using VpnHood.Common.Utils;

namespace VpnHood.Common.JobController;

public class JobSection
{
    private readonly object _lockObject = new();
    private bool _runnerEntered;
    private bool _normalEntered;
    private bool ShouldEnter => Elapsed > Interval && !_normalEntered;
    private bool ShouldRunnerEnterInternal => Elapsed > Interval && !_normalEntered && !_runnerEntered;
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

    internal bool ShouldRunnerEnter
    {
        get
        {
            lock (_lockObject)
                return ShouldRunnerEnterInternal;
        }
    }

    internal bool EnterRunner()
    {
        lock (_lockObject)
        {
            if (!ShouldRunnerEnterInternal)
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