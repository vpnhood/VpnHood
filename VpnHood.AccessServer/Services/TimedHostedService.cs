using System;
using System.Threading;
using System.Threading.Tasks;
using GrayMint.Common.JobController;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace VpnHood.AccessServer.Services;

public class TimedHostedService : IHostedService, IJob
{
    private readonly ILogger<TimedHostedService> _logger;
    private readonly SyncService _syncService;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly JobRunner _jobRunner;
    private CancellationTokenSource _cancellationTokenSource = new();
    public JobSection JobSection { get; }

    public TimedHostedService(
        ILogger<TimedHostedService> logger,
        IOptions<AppOptions> appOptions,
        SyncService syncService,
        IServiceScopeFactory serviceScopeFactory
        )
    {
        _logger = logger;
        _syncService = syncService;
        _serviceScopeFactory = serviceScopeFactory;
        var interval = appOptions.Value.AutoMaintenanceInterval ?? TimeSpan.MaxValue;
        JobSection = new JobSection(interval);
        _jobRunner = new JobRunner(false, logger);
        if (_jobRunner.Interval < interval) _jobRunner.Interval = interval;
        _jobRunner.Add(this);
    }

    public Task StartAsync(CancellationToken stoppingToken)
    {
        _jobRunner.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken stoppingToken)
    {
        _jobRunner.Stop();
        _cancellationTokenSource.Cancel();
        return Task.CompletedTask;
    }

    public async Task RunJob()
    {
        _logger.LogInformation("Updating usage cycle...");
        await using (var scope = _serviceScopeFactory.CreateAsyncScope())
        {
            var usageCycleService = scope.ServiceProvider.GetRequiredService<UsageCycleService>();
            await usageCycleService.UpdateCycle();
        }

        _logger.LogInformation("Start syncing...");
        await _syncService.Sync();

        _logger.LogInformation("Maintenance job has been finished.");
    }
}