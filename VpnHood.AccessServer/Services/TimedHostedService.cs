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
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly JobRunner _jobRunner;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    public JobSection JobSection { get; }

    public TimedHostedService(
        ILogger<TimedHostedService> logger,
        IOptions<AppOptions> appOptions,
        IServiceScopeFactory serviceScopeFactory
        )
    {
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;
        var interval = appOptions.Value.AutoMaintenanceInterval ?? TimeSpan.MaxValue;
        JobSection = new JobSection(interval);
        _jobRunner = new JobRunner(false, logger);
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
        await using var scope = _serviceScopeFactory.CreateAsyncScope();
         
        var usageCycleService = scope.ServiceProvider.GetRequiredService<UsageCycleService>();
        await usageCycleService.UpdateCycle();

        _logger.LogInformation("Start syncing...");
        var syncService = scope.ServiceProvider.GetRequiredService<SyncService>();
        await syncService.Sync();

        _logger.LogInformation("Maintenance job has been finished.");
    }
}