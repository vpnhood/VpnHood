using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Timer = System.Timers.Timer;

namespace VpnHood.AccessServer;

public class TimedHostedService : IHostedService, IDisposable
{
    private int _executionCount;
    private readonly ILogger<TimedHostedService> _logger;
    private Timer? _timer;

    public TimedHostedService(ILogger<TimedHostedService> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation($"{nameof(TimedHostedService)} running.");
        _timer = new Timer();
        //_timer = new Timer(DoWork, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        return Task.CompletedTask;
    }

    private void DoWork(object? state)
    {
        var count = Interlocked.Increment(ref _executionCount);

        _logger.LogInformation(
            "Timed Hosted Service is working. Count: {Count}", count);
        Thread.Sleep(5000);
    }

    public Task StopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation($"{nameof(TimedHostedService)} is stopping.");

        //_timer?.Change(Timeout.Infinite, 0);

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}