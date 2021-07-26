using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer
{
    public static class PublicCycleHelper
    {
        public static string CurrentCycleId => DateTime.Now.ToString("yyyy:MM");
        
        public static async Task DeleteCycle(VhContext vhContext, string cycleId)
            => vhContext.PublicCycles.RemoveRange(await vhContext.PublicCycles.Where(e => e.PublicCycleId == cycleId).ToArrayAsync());

        public static async Task ResetCycleTraffics(VhContext vhContext)
        {
            // reset usage for users
            var sql = @$"
                    UPDATE  {nameof(AccessUsage)}
                       SET  {nameof(AccessUsage.CycleSentTraffic)} = 0, {nameof(AccessUsage.CycleReceivedTraffic)} = 0
                      FROM  {nameof(AccessToken)} AS T
                            INNER JOIN {nameof(AccessUsage)} AS CU ON T.{nameof(AccessToken.AccessTokenId)} = CU.{nameof(AccessUsage.AccessTokenId)}
                    ";
            await vhContext.Database.ExecuteSqlRawAsync(sql);
        }

        public static async Task AddCycle(VhContext vhContext, string cycleId)
            => await vhContext.PublicCycles.AddAsync(new PublicCycle { PublicCycleId = cycleId });


        public static async Task UpdateCycle(VhContext vhContext)
        {
            if (await vhContext.PublicCycles.AnyAsync(e => e.PublicCycleId == CurrentCycleId))
                return;

            // reset usage for users
            await ResetCycleTraffics(vhContext);

            // add current cycle
            await AddCycle(vhContext, CurrentCycleId);
        }
    }
}
