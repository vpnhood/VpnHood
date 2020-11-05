using Dapper;
using System;
using System.ComponentModel.DataAnnotations;
using System.Data.SqlClient;
using System.Threading.Tasks;
using System.Transactions;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Services
{
    public class PublicCycleService
    {
        public static string GetCurrentCycleId() => DateTime.Now.ToString("yyyy:mm");
        public static async Task UpdateCycle()
        {
            using var _trans = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
            using var connection = App.OpenConnection();

            // check ic current cycle added
            var currentCycleId = GetCurrentCycleId();
            var found = await connection.QuerySingleOrDefaultAsync<int>(@$"
                    SELECT 1 FROM {nameof(PublicCycle)} WHERE {nameof(PublicCycle.publicCycleId)} = @{nameof(currentCycleId)}
                ", new { currentCycleId = GetCurrentCycleId() });

            // reset cycles and add current cycles
            if (found == 0)
            {
                await connection.ExecuteAsync(@$"
                    UPDATE  {nameof(AccessUsage)}
                       SET  {nameof(AccessUsage.sentTraffic)} = 0, {nameof(AccessUsage.receivedTraffic)} = 0
                      FROM  {nameof(Token)} AS T
                            INNER JOIN {nameof(AccessUsage)} AS CU ON T.{nameof(Token.tokenId)} = CU.{nameof(AccessUsage.tokenId)}
                     WHERE  T.{nameof(Token.isPublic)} = 1;
                    ");

                await connection.ExecuteAsync(@$"
                        INSERT INTO {nameof(PublicCycle)} ({nameof(PublicCycle.publicCycleId)})
                        VALUES (@{nameof(currentCycleId)})
                    ", new { currentCycleId = GetCurrentCycleId() });
            }

            _trans.Complete();
        }
    }

}
