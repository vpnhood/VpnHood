using Dapper;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading.Tasks;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Services
{

    public class AccessTokenService
    {
        public Guid Id { get; private set; }
        public static AccessTokenService FromId(Guid id) => new AccessTokenService() { Id = id };

        public static async Task<AccessTokenService> CreatePublic(string serverEndPoint, string tokenName, long maxTraffic)
        {
            if (string.IsNullOrEmpty(serverEndPoint)) throw new ArgumentNullException(nameof(serverEndPoint));

            var tokenId = Guid.NewGuid();
            var sql = @$"
                    INSERT INTO {AccessToken.Table_} ({AccessToken.accessTokenId_}, {AccessToken.accessTokenName_}, 
                                {AccessToken.serverEndPoint_}, {AccessToken.maxTraffic_}, {AccessToken.isPublic_})
                    VALUES (@{nameof(tokenId)}, @{nameof(tokenName)}, 
                            @{nameof(serverEndPoint)}, @{nameof(maxTraffic)}, 1);
            ";

            using var sqlConnection = App.OpenConnection();
            await sqlConnection.QueryAsync(sql, new { tokenId, tokenName, serverEndPoint, maxTraffic});
            return FromId(tokenId);
        }

        public static async Task<AccessTokenService> CreatePrivate(string serverEndPoint, string tokenName, int maxTraffic, int maxClient, DateTime? endTime, int lifetime)
        {
            if (string.IsNullOrEmpty(serverEndPoint)) throw new ArgumentNullException(nameof(serverEndPoint));

            var tokenId = Guid.NewGuid();
            var sql = @$"
                    INSERT INTO {AccessToken.Table_}({AccessToken.accessTokenId_}, {AccessToken.accessTokenName_}, 
                                {AccessToken.serverEndPoint_}, {AccessToken.maxTraffic_}, {AccessToken.isPublic_}, {AccessToken.maxClient_},
                                {AccessToken.endTime_}, {AccessToken.lifeTime_})

                    VALUES (@{nameof(tokenId)}, @{nameof(tokenName)}, @{nameof(serverEndPoint)}, @{nameof(maxTraffic)}, 0, 
                            @{nameof(maxClient)}, @{nameof(endTime)}, @{nameof(lifetime)} );
            ";

            using var sqlConnection = App.OpenConnection();
            await sqlConnection.QueryAsync(sql, new { tokenId, tokenName, serverEndPoint, maxTraffic, maxClient, endTime, lifetime });
            return FromId(tokenId);
        }

        public async Task<AccessToken> GetAccessToken()
        {
            var sql = @$"
                SELECT 
                       T.{AccessToken.accessTokenId_}, 
                       T.{AccessToken.accessTokenName_},
                       T.{AccessToken.supportId_},
                       T.{AccessToken.secret_},
                       T.{AccessToken.serverEndPoint_},
                       T.{AccessToken.maxClient_},
                       T.{AccessToken.maxTraffic_},
                       T.{AccessToken.lifeTime_},
                       T.{AccessToken.endTime_},
                       T.{AccessToken.startTime_},
                       T.{AccessToken.isPublic_}
                FROM {AccessToken.Table_} AS T
                WHERE T.{AccessToken.accessTokenId_} = @{nameof(Id)}
                ";

            using var sqlConnection = App.OpenConnection();
            var ret = await sqlConnection.QuerySingleOrDefaultAsync<AccessToken>(sql, new { Id });
            if (ret == null) throw new KeyNotFoundException();
            return ret;
        }

        public async Task<AccessUsage> GetAccessUsage(string clientIp)
        {
            var sql = @$"
                SELECT 
                       CU.{AccessUsage.sentTraffic_},
                       CU.{AccessUsage.receivedTraffic_},
                       CU.{AccessUsage.totalSentTraffic_},
                       CU.{AccessUsage.totalReceivedTraffic_}
                FROM {AccessToken.Table_} AS T
                    LEFT JOIN {AccessUsage.Table_} AS CU
                        ON T.{AccessToken.accessTokenId_} = CU.{AccessUsage.accessTokenId_} AND (CU.{AccessUsage.clientIp_} IS NULL OR CU.{AccessUsage.clientIp_} = @{nameof(clientIp)})
                WHERE T.{AccessToken.accessTokenId_} = @{nameof(Id)}
                ";

            using var sqlConnection = App.OpenConnection();
            var result = await sqlConnection.QuerySingleOrDefaultAsync<AccessUsage>(sql, new { Id, clientIp });
            return result;
        }

        public async Task<AccessUsage> AddAccessUsage(string clientIp, long sentTraffic, long receivedTraffic)
        {
            using var sqlConnection = App.OpenConnection();

            // check cycle first
            await PublicCycleService.UpdateCycle();

            // update
            var accessUsage = await GetAccessUsage(clientIp: clientIp);
            accessUsage.sentTraffic += sentTraffic;
            accessUsage.receivedTraffic += receivedTraffic;
            accessUsage.totalSentTraffic += sentTraffic;
            accessUsage.totalReceivedTraffic += receivedTraffic;
            var param = new
            {
                Id,
                clientIp,
                accessUsage.sentTraffic,
                accessUsage.receivedTraffic,
                accessUsage.totalSentTraffic,
                accessUsage.totalReceivedTraffic
            };

            var sql = $@"
                    UPDATE  {AccessUsage.Table_}
                       SET  {AccessUsage.sentTraffic_} = @{AccessUsage.sentTraffic_}, 
                            {AccessUsage.receivedTraffic_} = @{AccessUsage.receivedTraffic_}, 
                            {AccessUsage.totalSentTraffic_} = @{AccessUsage.totalSentTraffic_}, 
                            {AccessUsage.totalReceivedTraffic_} = @{AccessUsage.totalReceivedTraffic_}
                     WHERE  {AccessUsage.accessTokenId_} = @{nameof(Id)} AND  {AccessUsage.clientIp_} = @{nameof(clientIp)}
                ";

            
            var affectedRecord = await sqlConnection.ExecuteAsync(sql, param);

            if (affectedRecord == 0)
            {
                sql = @$"
                    INSERT INTO {AccessUsage.Table_} ({AccessUsage.accessTokenId_}, {AccessUsage.clientIp_}, {AccessUsage.sentTraffic_}, 
                                {AccessUsage.receivedTraffic_}, {AccessUsage.totalSentTraffic_}, {AccessUsage.totalReceivedTraffic_})
                    VALUES (@{nameof(Id)}, @{nameof(clientIp)}, @{AccessUsage.sentTraffic_}, @{AccessUsage.receivedTraffic_}, @{AccessUsage.totalSentTraffic_}, @{AccessUsage.totalReceivedTraffic_});
                ";
                await sqlConnection.ExecuteAsync(sql, param);
            }

            return accessUsage;
        }
    }
}
