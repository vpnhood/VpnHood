using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VpnHood.Common.Utils;

namespace VpnHood.Common.Jobs;

public class JobRunner
{
    public ILogger Logger { get; set; }
    private int _currentMaxDegreeOfParallelism;
    private SemaphoreSlim _semaphore;
    private readonly LinkedList<WeakReference<IJob>> _jobRefs = [];
    private readonly List<WeakReference<IJob>> _deadJobs = [];
    private readonly List<IJob> _jobs = [];
    private Timer? _timer;
    private TimeSpan _interval = TimeSpan.FromSeconds(5);
    public bool IsStarted => _timer != null;
    public int MaxDegreeOfParallelism { get; set; } = 100;
    public static JobRunner Default => DefaultLazy.Value;
    private static readonly Lazy<JobRunner> DefaultLazy = new(() => new JobRunner());

    public TimeSpan Interval {
        get => _interval;
        set {
            _interval = value;
            if (!IsStarted) return;
            Stop();
            Start();
        }
    }

    public JobRunner(bool start = true, ILogger? logger = null)
    {
        _currentMaxDegreeOfParallelism = MaxDegreeOfParallelism;
        _semaphore = new SemaphoreSlim(MaxDegreeOfParallelism);
        Logger = logger ?? NullLogger.Instance;
        if (start)
            Start();
    }

    private void RunJobs()
    {
        IJob[] jobs;
        lock (_jobRefs) {
            // find jobs to run
            foreach (var jobRef in _jobRefs) {
                // the WatchDog object is dead
                if (!jobRef.TryGetTarget(out var job)) {
                    _deadJobs.Add(jobRef);
                    continue;
                }

                // The watch dog is busy
                if (job.JobSection.ShouldRunnerEnter)
                    _jobs.Add(job);
            }

            // clear dead watch dogs
            foreach (var item in _deadJobs)
                _jobRefs.Remove(item);

            // collect jobs from temporary list
            jobs = _jobs.ToArray();

            // clear temporary lists
            _jobs.Clear();
            _deadJobs.Clear();
        }

        if (jobs.Length > 0)
            _ = RunJobs(jobs);
    }

    private async Task RunJobs(IEnumerable<IJob> jobs)
    {
        // update MaxDegreeOfParallelism
        if (_currentMaxDegreeOfParallelism != MaxDegreeOfParallelism &&
            _semaphore.CurrentCount == _currentMaxDegreeOfParallelism) {
            _currentMaxDegreeOfParallelism = MaxDegreeOfParallelism;
            _semaphore = new SemaphoreSlim(MaxDegreeOfParallelism);
        }

        // run jobs
        foreach (var job in jobs) {
            await _semaphore.WaitAsync().VhConfigureAwait();
            _ = RunJob(job);
        }
    }

    private async Task RunJob(IJob job)
    {
        // The watch dog is busy
        if (!job.JobSection.EnterRunner()) {
            _semaphore.Release();
            return;
        }

        // run the job
        try {
            await job.RunJob().VhConfigureAwait();
        }
        catch (ObjectDisposedException) {
            // remove from jobs
            lock (_jobRefs) {
                var jobRef = _jobRefs.FirstOrDefault(x => x.TryGetTarget(out var target) && target == job);
                _jobRefs.Remove(jobRef);
            }
        }
        catch (Exception ex) {
            Logger.LogError(ex, "Could not run a job. JobName: {JobName}", job.JobSection.Name ?? "NoName");
        }
        finally {
            job.JobSection.Leave();
            _semaphore.Release();
        }
    }

    public void Add(IJob job)
    {
        lock (_jobRefs)
            _jobRefs.AddLast(new WeakReference<IJob>(job));
    }

    public void Remove(IJob job)
    {
        lock (_jobRefs) {
            var item = _jobRefs.FirstOrDefault(x => x.TryGetTarget(out var target) && target == job);
            if (item != null)
                _jobRefs.Remove(item);
        }
    }

    public void Start()
    {
        _timer?.Dispose();
        _timer = new Timer(_ => RunJobs(), null, TimeSpan.Zero, Interval);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public static Task RunNow(IJob job)
    {
        return job.JobSection.Enter(job.RunJob, true);
    }
}