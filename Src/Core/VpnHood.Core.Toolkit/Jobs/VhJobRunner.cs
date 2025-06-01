using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Toolkit.Jobs;

public class VhJobRunner
{
    private SemaphoreSlim _semaphore;
    private readonly LinkedList<WeakReference<VhJob>> _jobs = [];
    private static readonly Lazy<VhJobRunner> DefaultLazy = new(() => new VhJobRunner());
    private int _maxDegreeOfParallelism = 2;
    private readonly TimeSpan _cleanupTimeSpan = TimeSpan.FromSeconds(60);
    private DateTime _lastCleanupTime = FastDateTime.Now;

    public static VhJobRunner Default => DefaultLazy.Value;
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(5);
    public int MaxDegreeOfParallelism {
        get => _maxDegreeOfParallelism;
        set {
            if (value < 1) throw new ArgumentOutOfRangeException(nameof(value), "MaxDegreeOfParallelism must be greater than 0.");
            _maxDegreeOfParallelism = value;
            _semaphore = new SemaphoreSlim(_maxDegreeOfParallelism);
        }
    }

    public VhJobRunner()
    {
        _semaphore = new SemaphoreSlim(_maxDegreeOfParallelism);
        Task.Run(RunJobs);
    }


    private async Task RunJobs()
    {
        while (true) {
            await Task.Delay(Interval).VhConfigureAwait();

            // Periodic cleanup of dead jobs based on CleanupTimeSpan
            var now = FastDateTime.Now;
            if (now - _lastCleanupTime >= _cleanupTimeSpan) {
                RemoveDeadCallbacks();
                _lastCleanupTime = now;
            }

            // Run jobs
            await RunJobsInternal().VhConfigureAwait();
        }

        // ReSharper disable once FunctionNeverReturns
    }

    private async Task RunJobsInternal()
    {
        // copy all callbacks to a temporary list
        var jobCallbacks = GetReadyJobs();

        // run jobs
        foreach (var jobCallback in jobCallbacks) {
            await _semaphore.WaitAsync().VhConfigureAwait();
            _ = RunJob(jobCallback);
        }
    }

    private async Task RunJob(VhJob job)
    {
        try {
            await job.RunNow().VhConfigureAwait();
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

    private IList<VhJob> GetReadyJobs()
    {
        List<VhJob> jobs;
        lock (_jobs) {
            jobs = new List<VhJob>(_jobs.Count);
            foreach (var jobRef in _jobs) {
                if (jobRef.TryGetTarget(out var target) && target.IsReadyToRun) {
                    jobs.Add(target);
                }
            }
        }
        return jobs;
    }

    public void Add(VhJob job)
    {
        lock (_jobs)
            _jobs.AddLast(new WeakReference<VhJob>(job));
    }

    public void Remove(VhJob job)
    {
        lock (_jobs) {
            var item = _jobs.FirstOrDefault(x => x.TryGetTarget(out var target) && target == job);
            if (item != null)
                _jobs.Remove(item);
        }
    }
}