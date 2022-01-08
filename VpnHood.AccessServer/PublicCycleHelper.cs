using System;
using System.Linq;
using System.Threading.Tasks;
using System.Transactions;
using Microsoft.EntityFrameworkCore;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer;

public static class PublicCycleHelper
{
    private static string? _lastCycleIdCache;
    public static string CurrentCycleId => DateTime.UtcNow.ToString("yyyy:MM");

    public static async Task ResetCycleTraffics()
    {
        await using var vhContext = new VhContext();

        // reset usage for users
        var sql = @$"
                    UPDATE  {nameof(vhContext.AccessUsages)}
                       SET  {nameof(AccessUsageEx.CycleSentTraffic)} = 0, {nameof(AccessUsageEx.CycleReceivedTraffic)} = 0
                       WHERE {nameof(AccessUsageEx.IsLast)} = 1
                    ";
        await vhContext.Database.ExecuteSqlRawAsync(sql);
    }

    public static async Task DeleteCycle(string cycleId)
    {
        _lastCycleIdCache = null;
        await using var vhContext = new VhContext();
        vhContext.PublicCycles.RemoveRange(await vhContext.PublicCycles.Where(e => e.PublicCycleId == cycleId)
            .ToArrayAsync());
        await vhContext.SaveChangesAsync();
    }

    public static async Task UpdateCycle()
    {
        await using var vhContext = new VhContext();
        if (_lastCycleIdCache == CurrentCycleId)
            return;

        if (await vhContext.PublicCycles.AnyAsync(e => e.PublicCycleId == CurrentCycleId))
        {
            _lastCycleIdCache = CurrentCycleId;
            return;
        }

        using TransactionScope tran = new(TransactionScopeAsyncFlowOption.Enabled);

        // add current cycle
        await vhContext.PublicCycles.AddAsync(new PublicCycle {PublicCycleId = CurrentCycleId});

        // reset usage for users
        await ResetCycleTraffics();

        tran.Complete();
    }
}