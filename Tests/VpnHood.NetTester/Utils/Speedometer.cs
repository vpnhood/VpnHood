using System.Diagnostics;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Jobs;
using VpnHood.Common.Logging;
using VpnHood.Common.Utils;

namespace VpnHood.NetTester.Utils;

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
    private readonly DateTime _startTime;

    public JobSection JobSection { get; }

    public Speedometer(string name,
        TimeSpan? interval = null,
        bool packetCounter = false)
    {
        _startTime = DateTime.Now;
        _name = name;
        _packetCounter = packetCounter;
        _stopwatch.Start();
        JobSection = new JobSection(interval ?? TimeSpan.FromSeconds(1));
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
            if (_packetCounter)
                VhLogger.Instance.LogInformation(
                    _name +
                    " {Speed}, Success: {Success}, TotalSucceeded: {TotalSucceeded}, TotalFailed: {TotalFailed}, TotalBytes: {TotalBytes}",
                    VhUtil.FormatBits(1000 * curTransferSize / _stopwatch.ElapsedMilliseconds), curSucceededCount,
                    _succeededCount, _failedCount, VhUtil.FormatBytes(_transferSize));
            else
                VhLogger.Instance.LogInformation(
                    _name + " {Speed}, Total: {Total} ",
                    VhUtil.FormatBits(1000 * curTransferSize / _stopwatch.ElapsedMilliseconds),
                    VhUtil.FormatBytes(_transferSize));

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
        Report();
        VhLogger.Instance.LogInformation("Elapsed: {Elapsed}", (DateTime.Now - _startTime).ToString(@"hh\:mm\:ss"));
        JobRunner.Default.Remove(this);
    }
}