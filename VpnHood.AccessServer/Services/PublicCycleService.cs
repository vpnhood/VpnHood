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
        public static string GetCurrentCycleId() => DateTime.Now.ToString("yyyy:MM");
        public static async Task UpdateCycle()
        {
            using var _trans = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
            using var connection = App.OpenConnection();
            var currentCycleId = GetCurrentCycleId();

            var sql = "";

            // check ic current cycle added
            sql = @$"
                    SELECT 1 FROM {PublicCycle.Table_} WHERE {PublicCycle.publicCycleId_} = @{nameof(currentCycleId)}
                   ";
            var found = await connection.QuerySingleOrDefaultAsync<int>(sql, new { currentCycleId });

            // reset cycles and add current cycles
            if (found == 0)
            {
                sql = @$"
                    UPDATE  {AccessUsage.Table_}
                       SET  {AccessUsage.sentTraffic_} = 0, {AccessUsage.receivedTraffic_} = 0
                      FROM  {AccessToken.Table_} AS T
                            INNER JOIN {AccessUsage.Table_} AS CU ON T.{AccessToken.accessTokenId_} = CU.{AccessUsage.accessTokenId_}
                     WHERE  T.{AccessToken.isPublic_} = 1
                    ";
                await connection.ExecuteAsync(sql);

                sql = @$"
                    INSERT INTO {PublicCycle.Table_} ({PublicCycle.publicCycleId_})
                    VALUES (@{nameof(currentCycleId)})
                    ";
                await connection.ExecuteAsync(sql, new { currentCycleId });
            }

            _trans.Complete();
        }
    }

}
