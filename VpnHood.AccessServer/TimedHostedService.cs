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
    private readonly AppOptions _appOptions;
    private readonly UsageCycleManager _usageCycleManager;
    private readonly SyncManager _syncManager;
    private Timer? _timer;

    public TimedHostedService(
        ILogger<TimedHostedService> logger,
        IOptions<AppOptions> appOptions,
        UsageCycleManager usageCycleManager,
        SyncManager syncManager
        )
    {
        _logger = logger;
        _appOptions = appOptions.Value;
        _usageCycleManager = usageCycleManager;
        _syncManager = syncManager;
    }

    public Task StartAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation($"{nameof(TimedHostedService)} is {_appOptions.AutoMaintenanceInterval}");
        if (_appOptions.AutoMaintenanceInterval != null)
        {
            _timer = new Timer(state => _ = DoMaintenanceJob(), null, _appOptions.AutoMaintenanceInterval.Value, Timeout.InfiniteTimeSpan);
        }

        return Task.CompletedTask;
    }

    private async Task DoMaintenanceJob()
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
            _logger.LogInformation("AutoMaintenance error. Error: {error}", ex);
        }
        finally
        {
            if (_appOptions.AutoMaintenanceInterval != null)
                _timer?.Change(_appOptions.AutoMaintenanceInterval.Value, Timeout.InfiniteTimeSpan);
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