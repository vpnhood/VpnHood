using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Logging;

namespace VpnHood.Common.JobController;

public class JobRunner
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly LinkedList<WeakReference<IJob>> _jobRefs = new();
    private readonly List<WeakReference<IJob>> _deadWatchDogRefs = new();
    private Timer? _timer;

    private TimeSpan _interval = TimeSpan.FromSeconds(5);
    public TimeSpan Interval
    {
        get => _interval;
        set
        {
            _interval = value;
            if (!IsStarted) return;
            Stop();
            Start();
        }
    }

    public static JobRunner Default => DefaultLazy.Value;
    private static readonly Lazy<JobRunner> DefaultLazy = new(() => new JobRunner());

    public JobRunner()
    {
        Start();
    }

    public void TimerProc(object? _)
    {
        try
        {
            _semaphore.Wait();

            // run each watch dog
            foreach (var jobRef in _jobRefs)
            {
                // the WatchDog object is dead
                if (!jobRef.TryGetTarget(out var job))
                {
                    _deadWatchDogRefs.Add(jobRef);
                    continue;
                }

                // The watch dog is busy
                if (job.JobSection != null && !job.JobSection.EnterRunner())
                    continue;

                try
                {
                    job
                        .RunJob()
                        .ContinueWith(_ =>
                        {
                            job.JobSection?.Leave();
                        });
                }
                catch (ObjectDisposedException)
                {
                    _deadWatchDogRefs.Add(jobRef);
                }
                catch (Exception ex)
                {
                    VhLogger.Instance.LogError(ex, "Could not run a WatchDog.");
                }
            }

            // clear dead watch dogs
            foreach (var item in _deadWatchDogRefs)
                _jobRefs.Remove(item);

            _deadWatchDogRefs.Clear();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Add(IJob job)
    {
        _ = AddInternal(job);
    }

    private async Task AddInternal(IJob job)
    {
        try
        {
            await _semaphore.WaitAsync();
            _jobRefs.AddLast(new WeakReference<IJob>(job));
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public bool IsStarted => _timer != null;

    public void Start()
    {
        _timer?.Dispose();
        _timer = new Timer(TimerProc, null, Interval, Interval);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }
}