using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace VpnHood.AccessServer;

public class TimedHostedService : IHostedService, IDisposable
{
    private readonly ILogger<TimedHostedService> _logger;
    private readonly IOptions<AppOptions> _appOptions;
    private readonly UsageCycleManager _usageCycleManager;
    private readonly SyncManager _syncManager;
    private Timer? _timer;
    private readonly TimeSpan _timerInterval = TimeSpan.FromMinutes(1); //todo

    public TimedHostedService(
        ILogger<TimedHostedService> logger,
        IOptions<AppOptions> appOptions,
        UsageCycleManager usageCycleManager,
        SyncManager syncManager
        )
    {
        _logger = logger;
        _appOptions = appOptions;
        _usageCycleManager = usageCycleManager;
        _syncManager = syncManager;
    }

    public Task StartAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation($"{nameof(TimedHostedService)} is {_appOptions.Value.AutoMaintenance}");
        if (_appOptions.Value.AutoMaintenance)
        {
            _timer = new Timer(state => _ = DoWork(), null, _timerInterval, Timeout.InfiniteTimeSpan); 
        }

        return Task.CompletedTask;
    }

    private async Task DoWork()
    {
        try
        {
            _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

            if (!_usageCycleManager.IsBusy)
            {
                _logger.LogInformation("Checking usage cycle...");
                await _usageCycleManager.UpdateCycle();
            }

            if (!_syncManager.IsBusy)
            {
                _logger.LogInformation("Starting cleaning-up...");
                await _syncManager.Sync();
                _logger.LogInformation("Clean-up has been finished.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogInformation($"Cleanup error. Error: {ex}");
        }
        finally
        {
            _timer?.Change(_timerInterval, Timeout.InfiniteTimeSpan);
        }
    }

    public Task StopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation($"{nameof(TimedHostedService)} is stopping.");
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}