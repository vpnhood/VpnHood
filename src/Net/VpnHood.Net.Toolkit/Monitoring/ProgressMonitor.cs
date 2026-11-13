using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Net.Toolkit.Monitoring;

/// <summary>
/// Provides the progress of a run of tasks as a percentage that also moves with time, so it does not stall while a
/// batch of slow tasks runs: the larger of the share of tasks completed and the share of the time the batches may
/// take.
/// </summary>
/// <remarks>
/// A run of no tasks is complete, at 100. A task timeout that is not positive leaves the time out, and the percentage
/// follows the completed tasks alone. In a release build the percentage stays within 0 to 100.
/// </remarks>
/// <param name="totalTaskCount">The number of tasks.</param>
/// <param name="taskTimeout">How long one task may take; a batch may take as long.</param>
/// <param name="maxDegreeOfParallelism">How many tasks run at once, as a batch. The default is 1.</param>
/// <exception cref="ArgumentOutOfRangeException">
/// <paramref name="totalTaskCount"/> is negative, or <paramref name="maxDegreeOfParallelism"/> is less than 1.
/// </exception>
public class ProgressMonitor(
    int totalTaskCount,
    TimeSpan taskTimeout,
    int maxDegreeOfParallelism = 1)
{
    private readonly int _totalTaskCount = totalTaskCount >= 0
        ? totalTaskCount
        : throw new ArgumentOutOfRangeException(nameof(totalTaskCount), totalTaskCount, "The number of tasks cannot be negative.");

    // a batch of no tasks would divide by zero
    private readonly int _maxDegreeOfParallelism = maxDegreeOfParallelism >= 1
        ? maxDegreeOfParallelism
        : throw new ArgumentOutOfRangeException(nameof(maxDegreeOfParallelism), maxDegreeOfParallelism, "The parallelism must be at least 1.");

    private readonly TimeSpan _taskTimeout = taskTimeout;
    private readonly DateTime _startTime = FastDateTime.UtcNow;
    private DateTime _currentBatchStartTime = FastDateTime.UtcNow;
    private readonly Lock _incrementLock = new();
    private int _completedTaskCount;
    private int TotalBatches => (int)Math.Ceiling(_totalTaskCount / (double)_maxDegreeOfParallelism);
    private TimeSpan MaxDuration => TimeSpan.FromMilliseconds(_taskTimeout.TotalMilliseconds * TotalBatches);
    private int CurrentBatchIndex => _completedTaskCount / _maxDegreeOfParallelism;

    /// <summary>Gets the progress now.</summary>
    public ProgressStatus Progress => new(
        Completed: _completedTaskCount,
        Total: _totalTaskCount,
        StartedTime: _startTime,
        Percentage: ProgressPercentage);

    /// <summary>Counts a completed task.</summary>
    public void IncrementCompleted()
    {
        lock (_incrementLock) {
            _completedTaskCount++;
            if (_completedTaskCount % _maxDegreeOfParallelism == 0)
                _currentBatchStartTime = FastDateTime.UtcNow;
        }
    }

    /// <summary>Counts every task as completed.</summary>
    public void Finish()
    {
        lock (_incrementLock)
            _completedTaskCount = _totalTaskCount;
    }

    private int ProgressPercentage {
        get {
            lock (_incrementLock) {
                // nothing to run is a run that is complete; its shares below would be 0 of 0
                if (_totalTaskCount == 0)
                    return 100;

                // Method 1: Progress based on completed tasks
                var taskCompletionProgress = _completedTaskCount / (double)_totalTaskCount * 100.0;

                // Method 2: Overall time-based progress; without a time to share, the tasks alone tell
                // completedTime not exceed of its own bound
                var timeProgress = 0.0;
                if (_taskTimeout > TimeSpan.Zero) {
                    var completedTime = CurrentBatchIndex * _taskTimeout + (FastDateTime.UtcNow - _currentBatchStartTime);
                    var maxCurrentBatchTime = (CurrentBatchIndex + 1) * _taskTimeout;
                    if (completedTime > maxCurrentBatchTime)
                        completedTime = maxCurrentBatchTime;

                    timeProgress = completedTime / MaxDuration * 100.0;
                }

                // Use the maximum of all three methods to ensure smooth progress that never goes backward
                var progress = Math.Max(taskCompletionProgress, timeProgress);
#if !DEBUG
                progress = Math.Min(100, progress);
                progress = Math.Max(0, progress);
#endif
                return (int)progress;
            }
        }
    }
}