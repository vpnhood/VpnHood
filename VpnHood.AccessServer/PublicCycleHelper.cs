using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Transactions;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer
{
    public static class PublicCycleHelper
    {
        public static string CurrentCycleId => DateTime.Now.ToString("yyyy:MM");

        public static async Task ResetCycleTraffics(VhContext vhContext)
        {
            // reset usage for users
            var sql = @$"
                    UPDATE  {nameof(vhContext.AccessUsages)}
                       SET  {nameof(AccessUsage.CycleSentTraffic)} = 0, {nameof(AccessUsage.CycleReceivedTraffic)} = 0
                    ";
            await vhContext.Database.ExecuteSqlRawAsync(sql);
        }

        public static async Task AddCycle(VhContext vhContext, string cycleId)
            => await vhContext.PublicCycles.AddAsync(new PublicCycle { PublicCycleId = cycleId });

        public static async Task DeleteCycle(VhContext vhContext, string cycleId)
            => vhContext.PublicCycles.RemoveRange(await vhContext.PublicCycles.Where(e => e.PublicCycleId == cycleId).ToArrayAsync());

        public static async Task UpdateCycle(VhContext vhContext)
        {

            if (await vhContext.PublicCycles.AnyAsync(e => e.PublicCycleId == CurrentCycleId))
                return;

            using TransactionScope tran = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

            // add current cycle
            await AddCycle(vhContext, CurrentCycleId);
            
            // reset usage for users
            await ResetCycleTraffics(vhContext);

            tran.Complete();
        }
    }
}
