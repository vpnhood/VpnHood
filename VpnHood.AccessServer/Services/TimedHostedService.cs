using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VpnHood.Common.Logging;

namespace VpnHood.AccessServer.Services;

public class TimedHostedService : IHostedService, IDisposable
{
    private readonly ILogger<TimedHostedService> _logger;
    private readonly AppOptions _appOptions;
    private readonly UsageCycleService _usageCycleService;
    private readonly SyncService _syncService;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private Timer? _timer;

    public TimedHostedService(
        ILogger<TimedHostedService> logger,
        IOptions<AppOptions> appOptions,
        UsageCycleService usageCycleService,
        SyncService syncService,
        IServiceScopeFactory serviceScopeFactory
        )
    {
        _logger = logger;
        _appOptions = appOptions.Value;
        _usageCycleService = usageCycleService;
        _syncService = syncService;
        _serviceScopeFactory = serviceScopeFactory;
    }

    public Task StartAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation($"{VhLogger.FormatTypeName(this)} is {_appOptions.AutoMaintenanceInterval}");
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

            _logger.LogInformation("Updating usage cycle...");
            await _usageCycleService.UpdateCycle();

            _logger.LogInformation("Start syncing...");
            await _syncService.Sync();
            _logger.LogInformation("Sync has been finished.");

            // maintenance job
            await using var scope = _serviceScopeFactory.CreateAsyncScope();
            var maintenanceService = scope.ServiceProvider.GetRequiredService<MaintenanceService>();
            await maintenanceService.RunJob();

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