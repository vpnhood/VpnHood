using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Toolkit.Jobs;

public class JobRunner
{
    private SemaphoreSlim _semaphore;
    private readonly LinkedList<WeakReference<Job>> _jobs = [];
    private static readonly Lazy<JobRunner> SlowInstanceLazy = new(() => new JobRunner(TimeSpan.FromSeconds(10)));
    private static readonly Lazy<JobRunner> FastInstanceLazy = new(() => new JobRunner(TimeSpan.FromSeconds(2)));
    private int _maxDegreeOfParallelism = 2;
    private readonly TimeSpan _cleanupTimeSpan = TimeSpan.FromSeconds(60);
    private DateTime _lastCleanupTime = FastDateTime.Now;

    public static JobRunner SlowInstance => SlowInstanceLazy.Value;
    public static JobRunner FastInstance => FastInstanceLazy.Value;
    public TimeSpan Interval { get; set; }
    public int MaxDegreeOfParallelism {
        get => _maxDegreeOfParallelism;
        set {
            if (value < 1) throw new ArgumentOutOfRangeException(nameof(value), "MaxDegreeOfParallelism must be greater than 0.");
            _maxDegreeOfParallelism = value;
            _semaphore = new SemaphoreSlim(_maxDegreeOfParallelism);
        }
    }

    public JobRunner(TimeSpan interval)
    {
        Interval = interval;
        _semaphore = new SemaphoreSlim(_maxDegreeOfParallelism);
        Task.Run(RunJobs);
    }


    private async Task RunJobs()
    {
        while (true) {
            await Task.Delay(Interval).Vhc();

            // Periodic cleanup of dead jobs based on CleanupTimeSpan
            var now = FastDateTime.Now;
            if (now - _lastCleanupTime >= _cleanupTimeSpan) {
                RemoveDeadCallbacks();
                _lastCleanupTime = now;
            }

            // Run jobs
            await RunJobsInternal().Vhc();
        }

        // ReSharper disable once FunctionNeverReturns
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
        catch (ObjectDisposedException) {
            Remove(job);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogCritical(ex, "JobCallback should not throw this exception.");
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
                if (!node.Value.TryGetTarget(out _)) {
                    VhLogger.Instance.LogDebug("Removing a dead job. Ensure proper disposal by the caller.");
                    _jobs.Remove(node);
                }

                node = next;
            }
        }
    }

    private IList<Job> GetReadyJobs()
    {
        List<Job> jobs;
        lock (_jobs) {
            jobs = new List<Job>(_jobs.Count);
            foreach (var jobRef in _jobs) {
                if (jobRef.TryGetTarget(out var target) && target.IsReadyToRun) {
                    jobs.Add(target);
                }
            }
        }
        return jobs;
    }

    public void Add(Job job)
    {
        lock (_jobs)
            _jobs.AddLast(new WeakReference<Job>(job));
    }

    public void Remove(Job job)
    {
        lock (_jobs) {
            var item = _jobs.FirstOrDefault(x => x.TryGetTarget(out var target) && target == job);
            if (item != null)
                _jobs.Remove(item);
        }
    }
}