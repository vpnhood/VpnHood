using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Net.Toolkit.Monitoring;

/// <summary>
/// Provides the progress of a run of tasks as a percentage that also moves with time, so it does not stall while a
/// batch of slow tasks runs: the larger of the share of tasks completed and the share of the time the batches may
/// take.
/// </summary>
/// <remarks>In a release build, the percentage stays within 0 to 100, given at least one task, a positive task timeout and a positive parallelism.</remarks>
/// <param name="totalTaskCount">The number of tasks.</param>
/// <param name="taskTimeout">How long one task may take; a batch may take as long.</param>
/// <param name="maxDegreeOfParallelism">How many tasks run at once, as a batch. The default is 1.</param>
public class ProgressMonitor(
    int totalTaskCount,
    TimeSpan taskTimeout,
    int maxDegreeOfParallelism = 1)
{
    private readonly DateTime _startTime = FastDateTime.UtcNow;
    private DateTime _currentBatchStartTime = FastDateTime.UtcNow;
    private readonly Lock _incrementLock = new();
    private int _completedTaskCount;
    private int TotalBatches => (int)Math.Ceiling(totalTaskCount / (double)maxDegreeOfParallelism);
    private TimeSpan MaxDuration => TimeSpan.FromMilliseconds(taskTimeout.TotalMilliseconds * TotalBatches);
    private int CurrentBatchIndex => _completedTaskCount / maxDegreeOfParallelism;

    /// <summary>Gets the progress now.</summary>
    public ProgressStatus Progress => new(
        Completed: _completedTaskCount,
        Total: totalTaskCount,
        StartedTime: _startTime,
        Percentage: ProgressPercentage);

    /// <summary>Counts a completed task.</summary>
    public void IncrementCompleted()
    {
        lock (_incrementLock) {
            _completedTaskCount++;
            if (_completedTaskCount % maxDegreeOfParallelism == 0)
                _currentBatchStartTime = FastDateTime.UtcNow;
        }
    }

    /// <summary>Counts every task as completed.</summary>
    public void Finish()
    {
        lock (_incrementLock)
            _completedTaskCount = totalTaskCount;
    }

    private int ProgressPercentage {
        get {
            lock (_incrementLock) {
                // Method 1: Progress based on completed tasks
                var taskCompletionProgress = _completedTaskCount / (double)totalTaskCount * 100.0;

                // Method 2: Overall time-based progress
                // completedTime not exceed of its own bound
                var completedTime = CurrentBatchIndex * taskTimeout + (FastDateTime.UtcNow - _currentBatchStartTime);
                var maxCurrentBatchTime = (CurrentBatchIndex + 1) * taskTimeout;
                if (completedTime > maxCurrentBatchTime)
                    completedTime = maxCurrentBatchTime;

                var timeProgress = completedTime / MaxDuration * 100.0;

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