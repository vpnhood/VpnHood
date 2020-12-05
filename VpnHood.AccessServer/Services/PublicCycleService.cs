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
            var currentCycleId = GetCurrentCycleId();

            var sql = "";

            // check ic current cycle added
            sql = @$"
                    SELECT 1 FROM {PublicCycle.Table_} WHERE {PublicCycle.publicCycleId_} = @{nameof(currentCycleId)}
                   ";

            using var sqlConnection = App.OpenConnection();
            var found = await sqlConnection.QuerySingleOrDefaultAsync<int>(sql, new { currentCycleId });

            // if current cycle not added yet
            if (found == 0)
            {
                // reset usage for users
                sql = @$"
                    UPDATE  {AccessUsage.Table_}
                       SET  {AccessUsage.sentTraffic_} = 0, {AccessUsage.receivedTraffic_} = 0
                      FROM  {AccessToken.Table_} AS T
                            INNER JOIN {AccessUsage.Table_} AS CU ON T.{AccessToken.accessTokenId_} = CU.{AccessUsage.accessTokenId_}
                     WHERE  T.{AccessToken.isPublic_} = 1
                    ";
                await sqlConnection.ExecuteAsync(sql);

                // add current cycle
                sql = @$"
                    INSERT INTO {PublicCycle.Table_} ({PublicCycle.publicCycleId_})
                    VALUES (@{nameof(currentCycleId)})
                    ";
                await sqlConnection.ExecuteAsync(sql, new { currentCycleId });
            }

            _trans.Complete();
        }
    }

}
