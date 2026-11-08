using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Net.Toolkit.Jobs;

/// <summary>
/// Represents a function that a <see cref="JobRunner"/> runs periodically: first after a due time, then at an interval
/// from the end of each run, never two runs at once.
/// </summary>
/// <remarks>
/// The runner checks its jobs at its own interval, so its polling and its busy run slots may delay a run: the shared fast runner checks every 2 seconds, the slow one, which takes the jobs with an
/// interval of 10 seconds or more, every 10 seconds. The runner holds a job weakly: keep a reference while it should
/// run, and dispose it to end it. A function's exception counts as a failure, a cancellation too unless the run's token is
/// canceled, and the runner logs it as critical; an <see cref="ObjectDisposedException"/> makes the runner drop the job
/// instead.
/// </remarks>
public class Job : IDisposable
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly SemaphoreSlim _jobSemaphore = new(1, 1);
    private readonly Func<CancellationToken, ValueTask> _jobFunc;
    private readonly TimeSpan _dueTime;
    private readonly int? _maxRetry;
    private long _currentFailedCount;
    private bool _disposed;
    private readonly JobRunner _jobRunner;
    /// <summary>Gets or sets the time from the end of a run to the next one.</summary>
    public TimeSpan Interval { get; set; }

    /// <summary>Gets the number of runs that succeeded.</summary>
    public long SucceededCount { get; private set; }

    /// <summary>
    /// Gets the number of runs that failed; an <see cref="OperationCanceledException"/> is not counted when the run's
    /// token is canceled.
    /// </summary>
    public long FailedCount { get; private set; }

    /// <summary>Gets a value indicating whether the job is started.</summary>
    public bool IsStarted => StartedTime != null;

    /// <summary>
    /// Gets or sets when the job was started, in UTC, which its due time counts from, or <see langword="null"/> while
    /// it is stopped.
    /// </summary>
    public DateTime? StartedTime { get; set; }

    /// <summary>Gets when the last run ended, in UTC, or <see langword="null"/> before the first.</summary>
    public DateTime? LastExecutedTime { get; private set; }

    /// <summary>Gets the job's name, for the log.</summary>
    public string Name { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Job"/> class, and adds it to a runner.
    /// </summary>
    /// <param name="jobFunc">
    /// The function to run. Its token is canceled when the job is disposed, or when a <see cref="RunNow(CancellationToken)"/>
    /// caller cancels.
    /// </param>
    /// <param name="options">The job's options.</param>
    /// <param name="jobRunner">
    /// The runner, or <see langword="null"/> for the shared one that fits the interval: <see cref="JobRunner.SlowInstance"/>
    /// from 10 seconds up, else <see cref="JobRunner.FastInstance"/>.
    /// </param>
    public Job(
        Func<CancellationToken, ValueTask> jobFunc,
        JobOptions options,
        JobRunner? jobRunner = null)
    {
        _jobFunc = jobFunc;
        _dueTime = options.DueTime ?? options.Interval;
        Interval = options.Interval;
        _maxRetry = options.MaxRetry;
        Name = options.Name ?? "NoName";
        if (options.AutoStart)
            Start();

        // initialize job runner based on the period
        if (jobRunner != null) {
            _jobRunner = jobRunner;
        }
        else {
            _jobRunner = options.Interval >= JobRunner.SlowInstance.Interval
                ? JobRunner.SlowInstance
                : JobRunner.FastInstance;
        }

        _jobRunner.Add(this);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Job"/> class that starts at once, runs at its runner's next check,
    /// and then every period.
    /// </summary>
    /// <param name="jobFunc">The function to run. Its token is canceled when the job is disposed.</param>
    /// <param name="period">The time from the end of a run to the next one.</param>
    /// <param name="name">The job's name, for the log, or <see langword="null"/>.</param>
    public Job(Func<CancellationToken, ValueTask> jobFunc, TimeSpan period, string? name = null)
        : this(jobFunc, new JobOptions {
            Interval = period,
            Name = name,
            DueTime = TimeSpan.Zero
        })
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Job"/> class that starts at once, runs at its runner's next check,
    /// and then every <see cref="JobOptions.DefaultInterval"/>.
    /// </summary>
    /// <param name="jobFunc">The function to run. Its token is canceled when the job is disposed.</param>
    /// <param name="name">The job's name, for the log, or <see langword="null"/>.</param>
    public Job(Func<CancellationToken, ValueTask> jobFunc, string? name = null)
        : this(jobFunc, new JobOptions {
            Name = name,
            DueTime = TimeSpan.Zero
        })
    {
    }

    /// <summary>
    /// Starts the job: its first run comes after the due time, counted from now; after an earlier run, after the interval
    /// from that run's end.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The job is disposed.</exception>
    /// <exception cref="InvalidOperationException">The job is started already.</exception>
    public void Start()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Job));

        if (IsStarted)
            throw new InvalidOperationException("Job is already started.");

        StartedTime = FastDateTime.UtcNow;
    }

    /// <summary>Stops the job and resets its count of failures in a row; a run under way goes on.</summary>
    /// <exception cref="InvalidOperationException">The job is not started.</exception>
    public void Stop()
    {
        if (!IsStarted)
            throw new InvalidOperationException("Job is not started.");

        StartedTime = null;
        _currentFailedCount = 0;
    }

    /// <summary>Gets a value indicating whether the job is started, not running, and due.</summary>
    public bool IsReadyToRun {
        get {
            // job is not started
            if (StartedTime is null)
                return false;

            // Someone else is currently running the job (semaphore is taken)
            if (_jobSemaphore.CurrentCount == 0)
                return false;

            var now = FastDateTime.UtcNow;

            // first time execution after due time 
            if (LastExecutedTime is null)
                return now - StartedTime >= _dueTime;

            // interval execution
            return now - LastExecutedTime >= Interval;
        }
    }

    /// <summary>
    /// Runs the job now, started or not, once a run under way ends. A cancellation by the job's disposal while the function
    /// runs ends it quietly.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="ObjectDisposedException">The job is disposed.</exception>
    /// <exception cref="OperationCanceledException">
    /// The run was canceled before the function started, as while it waited for a run under way to end.
    /// </exception>
    /// <exception cref="Exception">The function's exception, which also counts as a failure.</exception>
    public Task RunNow()
    {
        return RunInternal(_cancellationTokenSource.Token);
    }

    /// <summary>
    /// Runs the job now, started or not, once a run under way ends. A cancellation, by the token or by the job's disposal,
    /// while the function runs ends it quietly.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="ObjectDisposedException">The job is disposed.</exception>
    /// <exception cref="OperationCanceledException">
    /// The run was canceled before the function started, as while it waited for a run under way to end.
    /// </exception>
    /// <exception cref="Exception">The function's exception, which also counts as a failure.</exception>
    public async Task RunNow(CancellationToken cancellationToken)
    {
        using var linkedCts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cancellationTokenSource.Token);

        // await required for linkedCts to be disposed properly
        await RunInternal(linkedCts.Token).Vhc();
    }

    private async Task RunInternal(CancellationToken cancellationToken)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Job));

        // wait until we can run the job
        await _jobSemaphore.WaitAsync(cancellationToken).Vhc();

        try {
            await _jobFunc(cancellationToken).Vhc();
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
                _jobRunner.Logger?.LogError(ex,
                    "Job failed too many times and stopped. " +
                    "JobName: {JobName}, FailedCount: {FailedCount}, TotalErrorCount: {TotalFailedCount}",
                    Name, _currentFailedCount, FailedCount);

                Stop();
            }

            throw;
        }
        finally {
            LastExecutedTime = FastDateTime.UtcNow;
            VhInvoker.TryInvoke("release the job's semaphore", LogLevel.Debug, () => _jobSemaphore.Release()); // may be disposed
        }
    }

    /// <summary>
    /// Cancels a run under way, stops the job and removes it from its runner. Calling it again does nothing.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        _cancellationTokenSource.TryCancel();
        _cancellationTokenSource.Dispose();
        _jobSemaphore.Dispose();
        StartedTime = null;
        _jobRunner.Remove(this);
    }
}