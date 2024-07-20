using GrayMint.Common.AspNetCore.Jobs;
using Microsoft.EntityFrameworkCore;
using VpnHood.AccessServer.Clients;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Persistence.Models;

namespace VpnHood.AccessServer.Services;

public class UsageCycleService(
    ILogger<UsageCycleService> logger,
    VhContext vhContext,
    AgentCacheClient agentCacheClient)
    : IGrayMintJob
{
    private string? _lastCycleIdCache;

    public string CurrentCycleId => DateTime.UtcNow.ToString("yyyy:MM");

    public async Task DeleteCycle(string cycleId)
    {
        await vhContext.PublicCycles
            .Where(x => x.PublicCycleId == cycleId)
            .ExecuteDeleteAsync();

        _lastCycleIdCache = null;
    }

    public async Task UpdateCycle()
    {
        // check is current cycle already processed from cache
        if (_lastCycleIdCache == CurrentCycleId)
            return;

        logger.LogInformation("Checking usage cycles. CurrentCycleId: {CurrentCycleId}", CurrentCycleId);

        // check is current cycle already processed from db
        if (await vhContext.PublicCycles.AnyAsync(e => e.PublicCycleId == CurrentCycleId)) {
            _lastCycleIdCache = CurrentCycleId;
            return;
        }

        logger.LogInformation("Resetting usage cycles. CurrentCycleId: {CurrentCycleId}", CurrentCycleId);

        // reset usage for users
        // it must be done by 1 hour
        vhContext.Database.SetCommandTimeout(TimeSpan.FromMinutes(120));
        await vhContext.Accesses
            .Where(access => access.CycleTraffic > 0)
            .ExecuteUpdateAsync(p => p
                .SetProperty(access => access.LastCycleSentTraffic, access => access.TotalSentTraffic)
                .SetProperty(access => access.LastCycleReceivedTraffic, access => access.TotalReceivedTraffic));

        // add current cycle
        await vhContext.PublicCycles.AddAsync(new PublicCycleModel { PublicCycleId = CurrentCycleId });
        await vhContext.SaveChangesAsync();

        // clear all active sessions
        await agentCacheClient.InvalidateSessions();

        _lastCycleIdCache = CurrentCycleId;
        logger.LogInformation("All usage cycles has been reset. CurrentCycleId: {CurrentCycleId}", CurrentCycleId);
    }

    public Task RunJob(CancellationToken cancellationToken)
    {
        return UpdateCycle();
    }
}