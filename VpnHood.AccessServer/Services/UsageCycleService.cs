using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.Clients;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Persistence;

namespace VpnHood.AccessServer.Services;

public class UsageCycleService
{
    private readonly ILogger<UsageCycleService> _logger;
    private string? _lastCycleIdCache;
    private readonly VhContext _vhContext;
    private readonly AgentCacheClient _agentCacheClient;

    public string CurrentCycleId => DateTime.UtcNow.ToString("yyyy:MM");

    public UsageCycleService(ILogger<UsageCycleService> logger, VhContext vhContext, AgentCacheClient agentCacheClient)
    {
        _logger = logger;
        _vhContext = vhContext;
        _agentCacheClient = agentCacheClient;
    }

    public async Task DeleteCycle(string cycleId)
    {
        await _vhContext.PublicCycles
            .Where(x => x.PublicCycleId == cycleId)
            .ExecuteDeleteAsync();
        _lastCycleIdCache = null;
    }

    public async Task UpdateCycle()
    {
        // check is current cycle already processed from cache
        if (_lastCycleIdCache == CurrentCycleId)
            return;

        _logger.LogTrace(AccessEventId.Cycle,
            "Checking usage cycles. CurrentCycleId: {CurrentCycleId}", CurrentCycleId);

        // check is current cycle already processed from db
        if (await _vhContext.PublicCycles.AnyAsync(e => e.PublicCycleId == CurrentCycleId))
        {
            _lastCycleIdCache = CurrentCycleId;
            return;
        }

        _logger.LogInformation(AccessEventId.Cycle,
            "Resetting usage cycles. CurrentCycleId: {CurrentCycleId}", CurrentCycleId);

        // reset usage for users
        // it must be done by 1 hour
        _vhContext.Database.SetCommandTimeout(TimeSpan.FromMinutes(120));
        await _vhContext.Accesses
            .Where(access => access.CycleTraffic > 0)
            .ExecuteUpdateAsync(p => p
            .SetProperty(access => access.LastCycleSentTraffic, access => access.TotalSentTraffic)
            .SetProperty(access => access.LastCycleReceivedTraffic, access => access.TotalReceivedTraffic));

        // add current cycle
        await _vhContext.PublicCycles.AddAsync(new PublicCycleModel { PublicCycleId = CurrentCycleId });
        await _vhContext.SaveChangesAsync();

        // clear all active sessions
        await _agentCacheClient.InvalidateSessions();

        _lastCycleIdCache = CurrentCycleId;
        _logger.LogInformation("All usage cycles has been reset. CurrentCycleId: {CurrentCycleId}", CurrentCycleId);

    }
}