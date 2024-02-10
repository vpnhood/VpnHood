using Microsoft.Extensions.Options;

namespace VpnHood.AccessServer.Agent.Services;

public class TimedHostedService(
    ILogger<TimedHostedService> logger,
    IOptions<AgentOptions> agentOptions,
    IServiceScopeFactory serviceScopeFactory)
    : IHostedService, IDisposable
{
    private readonly AgentOptions _agentOptions = agentOptions.Value;
    private Timer? _saveCacheTimer;


    public Task StartAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation($"{nameof(TimedHostedService)} is {_agentOptions.SaveCacheInterval}");
        _saveCacheTimer = new Timer(state => _ = SaveCacheJob(), null, _agentOptions.SaveCacheInterval, Timeout.InfiniteTimeSpan);

        return Task.CompletedTask;
    }

    private async Task SaveCacheJob()
    {
        try
        {
            _saveCacheTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            await SaveCacheChanges();
        }
        finally
        {
            _saveCacheTimer?.Change(_agentOptions.SaveCacheInterval, Timeout.InfiniteTimeSpan);
        }
    }

    public async Task SaveCacheChanges()
    {
        try
        {
            await using var scope = serviceScopeFactory.CreateAsyncScope();
            var cacheRepo = scope.ServiceProvider.GetRequiredService<CacheService>();
            await cacheRepo.SaveChanges();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not save cache.");
        }
    }

    public async Task StopAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation($"{nameof(TimedHostedService)} is stopping.");
        try
        {
            await SaveCacheChanges();
        }
        catch
        {
            // ignored
        }
    }

    public void Dispose()
    {
        _saveCacheTimer?.Dispose();
    }
}