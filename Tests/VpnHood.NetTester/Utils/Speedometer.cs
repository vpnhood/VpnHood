using System.Diagnostics;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Jobs;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.NetTester.Utils;

public class Speedometer : IDisposable
{
    private readonly string _name;
    private readonly bool _packetCounter;
    private readonly Stopwatch _stopwatch = new();
    private readonly Lock _lockObject = new();
    private readonly Job _reportJob;
    private long _succeededCount;
    private long _failedCount;
    private long _transferSize;
    private long _lastTransferSize;
    private long _lastSucceededCount;
    private readonly DateTime _startTime;

    public Speedometer(string name,
        TimeSpan? interval = null,
        bool packetCounter = false)
    {
        _startTime = DateTime.Now;
        _name = name;
        _packetCounter = packetCounter;
        _stopwatch.Start();
        _reportJob = new Job(ReportJob, interval ?? TimeSpan.FromSeconds(1), "Speedometer Report");
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

    private ValueTask ReportJob(CancellationToken cancellationToken)
    {
        Report();
        return ValueTask.CompletedTask;
    }

    public void Report()
    {
        lock (_lockObject) {
            if (_stopwatch.ElapsedMilliseconds == 0) 
                return;

            var curTransferSize = _transferSize - _lastTransferSize;
            var curSucceededCount = _succeededCount - _lastSucceededCount;
            if (_packetCounter)
                VhLogger.Instance.LogInformation(
                    _name +
                    " {Speed}, Success: {Success}, TotalSucceeded: {TotalSucceeded}, TotalFailed: {TotalFailed}, TotalBytes: {TotalBytes}",
                    VhUtils.FormatBits(1000 * curTransferSize / _stopwatch.ElapsedMilliseconds), curSucceededCount,
                    _succeededCount, _failedCount, VhUtils.FormatBytes(_transferSize));
            else
                VhLogger.Instance.LogInformation(
                    _name + " {Speed}, Total: {Total} ",
                    VhUtils.FormatBits(1000 * curTransferSize / _stopwatch.ElapsedMilliseconds),
                    VhUtils.FormatBytes(_transferSize));

            _lastTransferSize = _transferSize;
            _lastSucceededCount = _succeededCount;
            _stopwatch.Restart();
        }
    }


    public void Dispose()
    {
        _reportJob.Dispose();
        VhLogger.Instance.LogInformation("Elapsed: {Elapsed}", (DateTime.Now - _startTime).ToString(@"hh\:mm\:ss"));
    }
}