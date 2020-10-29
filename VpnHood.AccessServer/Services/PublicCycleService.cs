using Dapper;
using System;
using System.ComponentModel.DataAnnotations;
using System.Transactions;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Services
{
    public class PublicCycleService
    {
        public static string GetCurrentCycleId() => DateTime.Now.ToString("yyyy:mm");
        public static void UpdateCycle()
        {
            using var connection = App.OpenConnection();

            using var _trans = new TransactionScope();

            // check ic current cycle added
            var currentCycleId = GetCurrentCycleId();
            var found = connection.QuerySingleOrDefault<int>(@$"
                    SELECT 1 FROM {nameof(PublicCycle)} WHERE {nameof(PublicCycle.publicCycleId)} = @{nameof(currentCycleId)}
                ", new { currentCycleId = GetCurrentCycleId() });

            // reset cycles and add current cycles
            if (found == 0)
            {
                connection.Execute(@$"
                    UPDATE  {nameof(ClientUsage)}
                       SET  {nameof(ClientUsage.sentTraffic)} = 0, {nameof(ClientUsage.receivedTraffic)} = 0
                      FROM  {nameof(Token)} AS T
                            INNER JOIN {nameof(ClientUsage)} AS CU ON T.{nameof(Token.tokenId)} = CU.{nameof(ClientUsage.tokenId)}
                     WHERE  T.{nameof(Token.isPublic)} = 1;
                    ");

                connection.Execute(@$"
                        INSERT INTO {nameof(PublicCycle)} ({nameof(PublicCycle.publicCycleId)})
                        VALUES (@{nameof(currentCycleId)})
                    ", new { currentCycleId = GetCurrentCycleId() });
            }

            _trans.Complete();
        }
    }

}
