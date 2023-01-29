using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.Clients;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Persistence;

namespace VpnHood.AccessServer.Services;

public class UsageCycleService
{
    private readonly ILogger<UsageCycleService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private string? _lastCycleIdCache;

    public string CurrentCycleId => DateTime.UtcNow.ToString("yyyy:MM");

    public UsageCycleService(ILogger<UsageCycleService> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    private async Task ResetCycleTraffics(VhContext vhContext)
    {
        // it must be done by 1 hour
        vhContext.Database.SetCommandTimeout(TimeSpan.FromMinutes(60));
        const string sql = @$"
                    UPDATE  {nameof(vhContext.Accesses)}
                       SET  {nameof(AccessModel.LastCycleSentTraffic)} = {nameof(AccessModel.TotalSentTraffic)}, {nameof(AccessModel.LastCycleReceivedTraffic)} = {nameof(AccessModel.TotalReceivedTraffic)}
                     WHERE {nameof(AccessModel.CycleTraffic)} > 0
                    ";
        await vhContext.Database.ExecuteSqlRawAsync(sql);
    }

    public async Task DeleteCycle(string cycleId)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        await using var vhContext = scope.ServiceProvider.GetRequiredService<VhContext>();
        vhContext.PublicCycles.RemoveRange(await vhContext.PublicCycles.Where(e => e.PublicCycleId == cycleId).ToArrayAsync());
        await vhContext.SaveChangesAsync();
        _lastCycleIdCache = null;
    }

    public async Task UpdateCycle()
    {
        // check is current cycle already processed from cache
        if (_lastCycleIdCache == CurrentCycleId)
            return;

        _logger.LogInformation($"Checking usage cycles for {CurrentCycleId}...");

        // check is current cycle already processed from db
        await using var scope = _serviceProvider.CreateAsyncScope();
        await using var vhContext = scope.ServiceProvider.GetRequiredService<VhContext>();
        if (await vhContext.PublicCycles.AnyAsync(e => e.PublicCycleId == CurrentCycleId))
        {
            _lastCycleIdCache = CurrentCycleId;
            return;
        }

        _logger.LogInformation($"Resetting usage cycles for {CurrentCycleId}...");


        // reset usage for users
        await ResetCycleTraffics(vhContext);

        // add current cycle
        await vhContext.PublicCycles.AddAsync(new PublicCycleModel { PublicCycleId = CurrentCycleId });
        await vhContext.SaveChangesAsync();

        // clear all active sessions
        var agentCacheClient = scope.ServiceProvider.GetRequiredService<AgentCacheClient>();
        await agentCacheClient.InvalidateSessions();

        _lastCycleIdCache = CurrentCycleId;
        _logger.LogInformation($"All usage cycles for {CurrentCycleId} has been reset.");

    }
}