using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer;

public class UsageCycleManager
{
    private readonly ILogger<UsageCycleManager> _logger;
    private string? _lastCycleIdCache;
    private bool _isBusy;
    private readonly object _isBusyLock = new();

    public UsageCycleManager(ILogger<UsageCycleManager> logger)
    {
        _logger = logger;
    }

    public string CurrentCycleId => DateTime.UtcNow.ToString("yyyy:MM");

    private static async Task ResetCycleTraffics(VhContext vhContext)
    {
        // reset usage for users
        const string sql = @$"
                    UPDATE  {nameof(vhContext.AccessUsages)}
                       SET  {nameof(AccessUsageEx.CycleSentTraffic)} = 0, {nameof(AccessUsageEx.CycleReceivedTraffic)} = 0
                       WHERE {nameof(AccessUsageEx.IsLast)} = 1 and {nameof(AccessUsageEx.CycleReceivedTraffic)} <> 0
                    ";
        await vhContext.Database.ExecuteSqlRawAsync(sql);
    }

    public async Task DeleteCycle(string cycleId)
    {
        await using var vhContext = new VhContext();
        vhContext.PublicCycles.RemoveRange(await vhContext.PublicCycles.Where(e => e.PublicCycleId == cycleId).ToArrayAsync());
        await vhContext.SaveChangesAsync();
        _lastCycleIdCache = null;
    }

    public async Task UpdateCycle()
    {
        // check is current cycle already processed from cache
        if (_lastCycleIdCache == CurrentCycleId)
            return;

        try
        {
            lock (_isBusyLock)
            {
                if (_isBusy) return;
                _isBusy = true;
            }

            _logger.LogInformation($"Checking usage cycles for {CurrentCycleId}...");

            // check is current cycle already processed from db
            await using var vhContext = new VhContext();
            if (await vhContext.PublicCycles.AnyAsync(e => e.PublicCycleId == CurrentCycleId))
            {
                _lastCycleIdCache = CurrentCycleId;
                return;
            }

            _logger.LogInformation($"Resetting usage cycles for {CurrentCycleId}...");

            await using var transaction = await vhContext.Database.BeginTransactionAsync();

            // add current cycle
            await vhContext.PublicCycles.AddAsync(new PublicCycle { PublicCycleId = CurrentCycleId });

            // reset usage for users
            await ResetCycleTraffics(vhContext);

            _lastCycleIdCache = CurrentCycleId;
            await vhContext.SaveChangesAsync();
            await vhContext.Database.CommitTransactionAsync();

            _logger.LogInformation($"All usage cycles for {CurrentCycleId} has been reset.");
        }
        finally
        {
            lock (_isBusyLock)
            {
                _isBusy = false;
            }
        }
    }
}