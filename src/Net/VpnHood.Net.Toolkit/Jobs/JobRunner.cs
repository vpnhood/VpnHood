using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Net.Toolkit.Jobs;

/// <summary>
/// Provides a loop that checks its jobs at an interval and runs the ready ones, a few at a time.
/// </summary>
/// <remarks>
/// It holds its jobs weakly: a job no one else references is dropped, and logged at Debug. Its loop runs for the
/// process's life.
/// </remarks>
public class JobRunner
{
    private SemaphoreSlim _semaphore;
    private readonly LinkedList<JobItem> _jobs = [];
    private static readonly Lazy<JobRunner> SlowInstanceLazy = new(() => new JobRunner(TimeSpan.FromSeconds(10)));
    private static readonly Lazy<JobRunner> FastInstanceLazy = new(() => new JobRunner(TimeSpan.FromSeconds(2)));
    private int _maxDegreeOfParallelism = 2;
    private readonly TimeSpan _cleanupTimeSpan = TimeSpan.FromSeconds(60);
    private DateTime _lastCleanupTime = FastDateTime.UtcNow;
    private readonly Lock _intervalLock = new();

    // a new period restarts its countdown, so a new interval applies at once, even to a runner asleep on the old one
    private readonly PeriodicTimer _timer;

    /// <summary>Gets the shared runner that checks its jobs every 10 seconds.</summary>
    public static JobRunner SlowInstance => SlowInstanceLazy.Value;

    /// <summary>Gets the shared runner that checks its jobs every 2 seconds.</summary>
    public static JobRunner FastInstance => FastInstanceLazy.Value;

    /// <summary>
    /// Gets or sets the logger of the jobs' failures, or <see langword="null"/> for none. The default is the process's
    /// <c>VhLogger.Instance</c>.
    /// </summary>
    public ILogger? Logger { get; set; } = VhLogger.Instance;

    // Locked, as the timer stores its period and restarts its countdown in two steps. The same interval again changes
    // nothing: each set restarts the countdown, so repeated ones would keep putting off the tick
    /// <summary>
    /// Gets or sets how often the runner checks its jobs. A new interval restarts the countdown; the same one changes
    /// nothing.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The interval is not a timer's valid period.</exception>
    public TimeSpan Interval {
        get {
            lock (_intervalLock)
                return _timer.Period;
        }
        set {
            lock (_intervalLock) {
                if (value != _timer.Period)
                    _timer.Period = value;
            }
        }
    }

    /// <summary>Gets or sets how many jobs run at once. The default is 2.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public int MaxDegreeOfParallelism {
        get => _maxDegreeOfParallelism;
        set {
            if (value < 1)
                throw new ArgumentOutOfRangeException(nameof(value), "MaxDegreeOfParallelism must be greater than 0.");
            _maxDegreeOfParallelism = value;
            _semaphore = new SemaphoreSlim(_maxDegreeOfParallelism);
        }
    }

    /// <summary>Initializes a new instance of the <see cref="JobRunner"/> class and starts its loop.</summary>
    /// <param name="interval">How often the runner checks its jobs.</param>
    /// <exception cref="ArgumentOutOfRangeException">The interval is not a timer's valid period.</exception>
    public JobRunner(TimeSpan interval)
    {
        _timer = new PeriodicTimer(interval);
        _semaphore = new SemaphoreSlim(_maxDegreeOfParallelism);
        Task.Run(RunJobs);
    }


    private async Task RunJobs()
    {
        while (await _timer.WaitForNextTickAsync().Vhc()) {
            // Periodic cleanup of dead jobs based on CleanupTimeSpan
            var now = FastDateTime.UtcNow;
            if (now - _lastCleanupTime >= _cleanupTimeSpan) {
                RemoveDeadCallbacks();
                _lastCleanupTime = now;
            }

            // Run jobs
            await RunJobsInternal().Vhc();
        }
    }

    private async Task RunJobsInternal()
    {
        // copy all callbacks to a temporary list
        var jobCallbacks = GetReadyJobs();

        // run jobs
        foreach (var jobCallback in jobCallbacks) {
            await _semaphore.WaitAsync().Vhc();
            _ = RunJob(jobCallback);
        }
    }

    private async Task RunJob(Job job)
    {
        try {
            await job.RunNow().Vhc();
        }
        // a job whose owner is disposed throws it on purpose, to end itself; not an error
        catch (ObjectDisposedException) {
            Remove(job);
        }
        catch (Exception ex) {
            Logger?.LogCritical(ex, "JobCallback should not throw this exception.");
        }
        finally {
            _semaphore.Release();
        }
    }

    private void RemoveDeadCallbacks()
    {
        lock (_jobs) {
            var node = _jobs.First;
            while (node != null) {
                // store next before possibly removing current
                var next = node.Next;

                // if the WeakReference is dead, remove it
                if (!node.Value.JobReference.TryGetTarget(out _)) {
                    Logger?.LogDebug(
                        "Removing a dead job. Ensure proper disposal by the caller. JobName: {JobName}",
                        node.Value.Name);
                    _jobs.Remove(node);
                }

                node = next;
            }
        }
    }

    private IReadOnlyList<Job> GetReadyJobs()
    {
        List<Job> jobs;
        lock (_jobs) {
            jobs = new List<Job>(_jobs.Count);
            foreach (var jobRef in _jobs) {
                if (jobRef.JobReference.TryGetTarget(out var target) && target.IsReadyToRun) {
                    jobs.Add(target);
                }
            }
        }

        return jobs;
    }

    /// <summary>Adds a job, held weakly.</summary>
    /// <param name="job">The job.</param>
    public void Add(Job job)
    {
        lock (_jobs)
            _jobs.AddLast(new JobItem {
                Name = job.Name,
                JobReference = new WeakReference<Job>(job)
            });
    }

    /// <summary>Removes a job.</summary>
    /// <param name="job">The job.</param>
    public void Remove(Job job)
    {
        lock (_jobs) {
            var item = _jobs.FirstOrDefault(x => x.JobReference.TryGetTarget(out var target) && target == job);
            if (item != null)
                _jobs.Remove(item);
        }
    }

    private class JobItem
    {
        public required string Name { get; init; }
        public required WeakReference<Job> JobReference { get; init; }
    }
}