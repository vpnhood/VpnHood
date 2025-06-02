using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Toolkit.Jobs;

public class VhJob : IDisposable
{
    private readonly string _name;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly SemaphoreSlim _jobSemaphore = new(1, 1);
    private readonly Func<CancellationToken, ValueTask> _jobFunc;
    private readonly TimeSpan _dueTime;
    private readonly int? _maxRetry;
    private long _currentFailedCount;
    private bool _disposed;
    public TimeSpan Period { get; set; }
    public long SucceededCount { get; private set; }
    public long FailedCount { get; private set; }
    public bool IsStarted => StartedTime != null;
    public DateTime? StartedTime { get; set; }
    public DateTime? LastExecutedTime { get; private set; }

    public VhJob(Func<CancellationToken, ValueTask> jobFunc, VhJobOptions options)
    {
        _jobFunc = jobFunc;
        _dueTime = options.DueTime ?? options.Period;
        Period = options.Period;
        _maxRetry = options.MaxRetry;
        _name = options.Name ?? "NoName";
        if (options.AutoStart)
            Start();

        VhJobRunner.Default.Add(this);
    }

    public VhJob(Func<CancellationToken, ValueTask> jobFunc, TimeSpan period, string? name = null)
        : this(jobFunc, new VhJobOptions {
            Period = period,
            Name = name,
            DueTime = TimeSpan.Zero,
        })
    {
    }
    public VhJob(Func<CancellationToken, ValueTask> jobFunc, string? name = null)
        : this(jobFunc, new VhJobOptions {
            Name = name,
            DueTime = TimeSpan.Zero,
        })
    {
    }

    public void Start()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(VhJob));

        if (IsStarted)
            throw new InvalidOperationException("Job is already started.");

        StartedTime = FastDateTime.Now;
    }

    public void Stop()
    {
        if (!IsStarted)
            throw new InvalidOperationException("Job is not started.");

        StartedTime = null;
        _currentFailedCount = 0;
    }

    public bool IsReadyToRun {
        get {
            // job is not started
            if (StartedTime is null)
                return false;

            // Someone else is currently running the job (semaphore is taken)
            if (_jobSemaphore.CurrentCount == 0)
                return false;

            var now = FastDateTime.Now;

            // first time execution after due time 
            if (LastExecutedTime is null)
                return now - StartedTime >= _dueTime;

            // interval execution
            return now - LastExecutedTime >= Period;
        }
    }

    public Task RunNow()
    {
        return RunInternal(_cancellationTokenSource.Token);
    }

    public Task RunNow(CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cancellationTokenSource.Token);
        return RunInternal(linkedCts.Token);
    }

    private async Task RunInternal(CancellationToken cancellationToken)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(VhJob));

        try {
            await _jobSemaphore.WaitAsync(cancellationToken).VhConfigureAwait();
            await _jobFunc(cancellationToken).VhConfigureAwait();
            _currentFailedCount = 0;
            SucceededCount++;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            // job was cancelled and no logging is needed
        }
        catch (Exception ex) {
            FailedCount++;
            _currentFailedCount++;

            // stop the job if it has failed too many times
            if (_currentFailedCount > _maxRetry) {
                VhLogger.Instance.LogError(ex,
                    "Job failed too many times and stopped. JobName: {JobName}, FailedCount: {FailedCount}, TotalErrorCount: {TotalFailedCount}",
                    _name, _currentFailedCount, FailedCount);

                Stop();
            }

            throw;
        }
        finally {
            LastExecutedTime = FastDateTime.Now;
            _jobSemaphore.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
        _jobSemaphore.Dispose();
        StartedTime = null;
        VhJobRunner.Default.Remove(this);
    }
}