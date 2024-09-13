using System.Diagnostics;
using VpnHood.Common.Jobs;
using VpnHood.Common.Utils;

namespace VpnHood.NetTester;

public class Speedometer : IJob, IDisposable
{
    private readonly string _name;
    private readonly bool _packetCounter;
    private readonly Stopwatch _stopwatch = new();
    private readonly object _lockObject = new();

    private long _succeededCount;
    private long _failedCount;
    private long _transferSize;
    private long _lastTransferSize;
    private long _lastSucceededCount;

    public JobSection JobSection { get; }

    public Speedometer(string name,
        TimeSpan? interval = null,
        bool packetCounter = false)
    {
        _name = name;
        _packetCounter = packetCounter;
        _stopwatch.Start();
        JobSection = new JobSection(interval ?? TimeSpan.FromSeconds(2));
        JobRunner.Default.Add(this);
    }

    public void AddSucceeded(int bytes)
    {
        lock (_lockObject) {
            _transferSize += bytes;
            _succeededCount++;
        }
    }

    public void AddRead(int bytes)
    {
        lock (_lockObject) {
            _transferSize += bytes;
            _succeededCount++;
        }
    }

    public void AddWrite(int bytes)
    {
        lock (_lockObject) {
            _transferSize += bytes;
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
        lock (_lockObject) {
            if (_stopwatch.ElapsedMilliseconds == 0) return;

            var curTransferSize = _transferSize - _lastTransferSize;
            var curSucceededCount = _succeededCount - _lastSucceededCount;
            if (_packetCounter) {
                Console.WriteLine(_name + ": " +
                                  $"Speed: {VhUtil.FormatBits(1000 * curTransferSize / _stopwatch.ElapsedMilliseconds)}, " +
                                  $"Success: {curSucceededCount}, TotalSucceeded: {_succeededCount}, TotalFailed: {_failedCount}, TotalBytes: {VhUtil.FormatBytes(_transferSize)}");
            }
            else {
                Console.WriteLine(_name + ": " +
                                  $"Speed: {VhUtil.FormatBits(1000 * curTransferSize / _stopwatch.ElapsedMilliseconds)}, " +
                                  $"TotalBytes: {VhUtil.FormatBytes(_transferSize)}");
            }

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

    public void Dispose()
    {
        JobRunner.Default.Remove(this);
    }
}