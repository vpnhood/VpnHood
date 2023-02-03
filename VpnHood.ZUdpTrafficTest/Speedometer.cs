using System.Diagnostics;
using VpnHood.Common.JobController;
using VpnHood.Common.Utils;

namespace VpnHood.ZUdpTrafficTest;

public class Speedometer : IJob
{
    private readonly string _name;
    private readonly Stopwatch _stopwatch = new();
    private readonly object _lockObject = new();

    private long _succeededCount;
    private long _failedCount;
    private long _transferSize;
    private long _lastTransferSize;
    private long _lastSucceededCount;

    public JobSection JobSection { get; }
    
    public Speedometer(string name, TimeSpan? interval = null)
    {
        _name = name;
        _stopwatch.Start();
        JobSection = new JobSection(interval ?? TimeSpan.FromSeconds(2));
        JobRunner.Default.Add(this);
    }

    public void AddSucceeded(byte[] buffer)
    {
        lock (_lockObject)
        {
            _transferSize += buffer.Length;
            _succeededCount++;
        }
    }

    public void AddFailed()
    {
        lock (_lockObject)
            _failedCount++;
    }

    public void Report()
    {
        lock (_lockObject)
        {
            if (_stopwatch.ElapsedMilliseconds == 0) return;

            var curTransferSize = _transferSize - _lastTransferSize;
            var curSucceededCount = _succeededCount - _lastSucceededCount;
            Console.WriteLine(_name + ": " +
                              $"Transfer: {Util.FormatBits(1000 * curTransferSize / _stopwatch.ElapsedMilliseconds)}, " +
                              $"Success: {curSucceededCount}, TotalSucceeded: {_succeededCount}, TotalFailed: {_failedCount}, TotalBytes: {Util.FormatBytes(_transferSize)}");

            _lastTransferSize = _transferSize;
            _lastSucceededCount = _succeededCount;
            _stopwatch.Restart();
        }
    }

    public Task RunJob()
    {
        Report();
        return Task.CompletedTask;
    }

}