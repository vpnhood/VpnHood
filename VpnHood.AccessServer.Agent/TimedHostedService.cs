using Microsoft.Extensions.Options;
using VpnHood.AccessServer.Agent.Services;

namespace VpnHood.AccessServer.Agent;

public class TimedHostedService : IHostedService, IDisposable
{
    private readonly ILogger<TimedHostedService> _logger;
    private readonly AgentOptions _agentOptions;
    private Timer? _saveCacheTimer;
    private readonly IServiceScopeFactory _serviceScopeFactory;


    public TimedHostedService(
        ILogger<TimedHostedService> logger,
        IOptions<AgentOptions> agentOptions,
        IServiceScopeFactory serviceScopeFactory)
    {
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;
        _agentOptions = agentOptions.Value;
    }

    public Task StartAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation($"{nameof(TimedHostedService)} is {_agentOptions.SaveCacheInterval}");
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
            await using var vhContextScope = _serviceScopeFactory.CreateAsyncScope();
            var cacheRepo = vhContextScope.ServiceProvider.GetRequiredService<CacheService>();
            await cacheRepo.SaveChanges();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Could not save cache.");
        }
    }

    public async Task StopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation($"{nameof(TimedHostedService)} is stopping.");
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