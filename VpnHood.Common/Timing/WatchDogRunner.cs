using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VpnHood.Common.Logging;

namespace VpnHood.Common.Timing;

public class WatchDogRunner
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly LinkedList<WeakReference<IWatchDog>> _watchDogRefs = new();
    private readonly List<WeakReference<IWatchDog>> _deadWatchDogRefs = new();
    private Timer? _timer;

    private TimeSpan _interval = TimeSpan.FromSeconds(30);
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

    public static WatchDogRunner Default => DefaultLazy.Value;
    private static readonly Lazy<WatchDogRunner> DefaultLazy = new(() => new WatchDogRunner());

    public WatchDogRunner()
    {
        Start();
    }

    public void TimerProc(object? _)
    {
        try
        {
            _semaphore.Wait();

            // run each watch dog
            foreach (var watchDogRef in _watchDogRefs)
            {
                if (watchDogRef.TryGetTarget(out var watchDog))
                {
                    try
                    {
                        if (watchDog.WatchDogChecker == null || watchDog.WatchDogChecker.ShouldEnter)
                        {
                            watchDog
                                .DoWatch()
                                .ContinueWith(_ =>
                                {
                                    if (watchDog.WatchDogChecker?.AutoDone == true)
                                        watchDog.WatchDogChecker.Done();
                                });
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                        _deadWatchDogRefs.Add(watchDogRef);
                    }
                    catch (Exception ex)
                    {
                        VhLogger.Instance.LogError(ex, "Could not run a WatchDog.");
                    }
                }
                else
                {
                    _deadWatchDogRefs.Add(watchDogRef);
                }
            }

            // clear dead watch dogs
            foreach (var item in _deadWatchDogRefs)
                _watchDogRefs.Remove(item);

            _deadWatchDogRefs.Clear();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Add(IWatchDog watchDog)
    {
        _ = AddInternal(watchDog);
    }

    private async Task AddInternal(IWatchDog watchDog)
    {
        try
        {
            await _semaphore.WaitAsync();
            _watchDogRefs.AddLast(new WeakReference<IWatchDog>(watchDog));
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