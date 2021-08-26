﻿using System;
using System.Linq;
using System.Threading.Tasks;
using System.Transactions;
using Microsoft.EntityFrameworkCore;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer
{
    public static class PublicCycleHelper
    {
        public static string CurrentCycleId => DateTime.Now.ToString("yyyy:MM");
        private static string? _lastCycleIdCache;

        public static async Task ResetCycleTraffics()
        {
            await using VhContext vhContext = new();

            // reset usage for users
            var sql = @$"
                    UPDATE  {nameof(vhContext.AccessUsages)}
                       SET  {nameof(AccessUsage.CycleSentTraffic)} = 0, {nameof(AccessUsage.CycleReceivedTraffic)} = 0
                    ";
            await vhContext.Database.ExecuteSqlRawAsync(sql);
        }

        public static async Task DeleteCycle(string cycleId)
        {
            _lastCycleIdCache = null;
            await using VhContext vhContext = new();
            vhContext.PublicCycles.RemoveRange(await vhContext.PublicCycles.Where(e => e.PublicCycleId == cycleId).ToArrayAsync());
            await vhContext.SaveChangesAsync();
        }

        public static async Task UpdateCycle()
        {
            await using VhContext vhContext = new();
            if (_lastCycleIdCache == CurrentCycleId)
                return;

            if (await vhContext.PublicCycles.AnyAsync(e => e.PublicCycleId == CurrentCycleId))
            {
                _lastCycleIdCache = CurrentCycleId;
                return;
            }

            using TransactionScope tran = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

            // add current cycle
            await vhContext.PublicCycles.AddAsync(new PublicCycle { PublicCycleId = CurrentCycleId });

            // reset usage for users
            await ResetCycleTraffics();

            tran.Complete();
        }
    }
}
