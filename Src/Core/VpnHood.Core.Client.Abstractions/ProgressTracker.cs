using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Client.Abstractions;

public class ProgressTracker(
    int totalTaskCount,
    TimeSpan taskTimeout,
    int maxDegreeOfParallelism = 1)
{
    private readonly DateTime _startTime = FastDateTime.Now;
    private DateTime _currentBatchStartTime = FastDateTime.Now;
    private readonly object _incrementLock = new();
    private int _completedTaskCount;
    private int TotalBatches => (int)Math.Ceiling(totalTaskCount / (double)maxDegreeOfParallelism);
    private TimeSpan MaxDuration => TimeSpan.FromMilliseconds(taskTimeout.TotalMilliseconds * TotalBatches);
    private int CurrentBatchIndex => _completedTaskCount / maxDegreeOfParallelism;
    
    public ProgressStatus Progress => new(
        Completed: _completedTaskCount, 
        Total: totalTaskCount, 
        StartedTime: _startTime, 
        Percentage: ProgressPercentage);

    public void IncrementCompleted()
    {
        lock (_incrementLock) {
            _completedTaskCount++;
            if (_completedTaskCount % maxDegreeOfParallelism == 0)
                _currentBatchStartTime = FastDateTime.Now;
        }
    }

    // Mark as fully completed
    public void Finish()
    {
        lock (_incrementLock)
            _completedTaskCount = totalTaskCount;
    }

    private int ProgressPercentage {
        get {
            lock (_incrementLock) {

                // Method 1: Progress based on completed tasks
                var taskCompletionProgress = (_completedTaskCount / (double)totalTaskCount) * 100.0;

                // Method 2: Overall time-based progress
                var completedTime = CurrentBatchIndex * taskTimeout + (FastDateTime.Now - _currentBatchStartTime);
                var timeProgress = completedTime / MaxDuration * 100.0;

                // Use the maximum of all three methods to ensure smooth progress that never goes backward
                var progress = Math.Max(taskCompletionProgress, timeProgress);
#if !DEBUG
                progress = Math.Min(100, progress);
#endif
                return (int)progress;
            }
        }
    }
}