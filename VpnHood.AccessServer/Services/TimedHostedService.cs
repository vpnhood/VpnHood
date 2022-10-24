using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace VpnHood.AccessServer.Services;

public class TimedHostedService : IHostedService, IDisposable
{
    private readonly ILogger<TimedHostedService> _logger;
    private readonly AppOptions _appOptions;
    private readonly UsageCycleService _usageCycleService;
    private readonly SyncService _syncService;
    private Timer? _timer;

    public TimedHostedService(
        ILogger<TimedHostedService> logger,
        IOptions<AppOptions> appOptions,
        UsageCycleService usageCycleService,
        SyncService syncService
        )
    {
        _logger = logger;
        _appOptions = appOptions.Value;
        _usageCycleService = usageCycleService;
        _syncService = syncService;
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

            if (!_usageCycleService.IsBusy)
            {
                _logger.LogInformation("Checking usage cycle...");
                await _usageCycleService.UpdateCycle();
            }

            if (!_syncService.IsBusy)
            {
                _logger.LogInformation("Starting cleaning-up...");
                await _syncService.Sync();
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