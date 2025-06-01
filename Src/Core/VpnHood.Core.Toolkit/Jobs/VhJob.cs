using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Toolkit.Jobs;

public class VhJob : IDisposable
{
    private readonly string _name;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Func<CancellationToken, ValueTask> _jobFunc;
    private readonly TimeSpan _dueTime;
    private readonly TimeSpan _period;
    private readonly int? _maxRetry;
    private bool _disposed;
    public bool IsStarted { get; private set; }

    public VhJob(Func<CancellationToken, ValueTask> jobFunc, VhJobOptions options)
    {
        _jobFunc = jobFunc;
        _dueTime = options.DueTime ?? options.Period;
        _period = options.Period;
        _maxRetry = options.MaxRetry;
        _name = options.Name ?? "NoName";
        if (options.AutoStart)
            Start();
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
        if (IsStarted)
            throw new InvalidOperationException("Job is already started.");
        Task.Run(ReportTask);
    }

    private async Task ReportTask()
    {
        IsStarted = true;
        var errorCounter = 0;
        long counter = 0;
        while (!_disposed)
            try {
                // wait for cancellation or due time
                counter++;
                if (counter == 1 && _dueTime > TimeSpan.Zero)
                    await Task.Delay(_dueTime, _cancellationTokenSource.Token).VhConfigureAwait();

                // run the job
                await Task.Delay(_period, _cancellationTokenSource.Token).VhConfigureAwait();
                await _jobFunc(_cancellationTokenSource.Token);
            }
            catch (OperationCanceledException) when (_cancellationTokenSource.IsCancellationRequested) {
                // job was cancelled
                break;
            }
            catch (Exception ex) {
                errorCounter++;
                VhLogger.Instance.LogError(ex, "Could not run a job. JobName: {JobName}", _name);

                if (errorCounter > _maxRetry)
                    break;
            }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
        IsStarted = false;
    }
}