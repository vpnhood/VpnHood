using Dapper;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Services
{
    public class TokenService
    {
        public Guid Id { get; private set; }
        public static TokenService FromId(Guid id) => new TokenService() { Id = id };

        public static async Task<TokenService> CreatePublic(string tokenName, string dnsName, string serverEndPoint, long maxTraffic)
        {
            var tokenId = Guid.NewGuid();
            var sql = @$"
                    INSERT INTO {Token.Token_}({Token.tokenId_}, {Token.tokenName_}, 
                                {Token.dnsName_}, {Token.serverEndPoint_}, {Token.maxTraffic_}, {Token.isPublic_})

                    VALUES (@{nameof(tokenId)}, @{nameof(tokenName)}, @{nameof(dnsName)}, 
                            @{nameof(serverEndPoint)}, @{nameof(maxTraffic)}, 1);
            ";

            using var connection = App.OpenConnection();
            await connection.QueryAsync(sql, new { tokenId, tokenName, dnsName, serverEndPoint, maxTraffic });
            return FromId(tokenId);
        }

        public static async Task<TokenService> CreatePrivate(string tokenName, string dnsName, string serverEndPoint, int maxTraffic, int maxClient, DateTime? endTime = null, int lifetime = 0)
        {
            var tokenId = Guid.NewGuid();
            var sql = @$"
                    INSERT INTO {Token.Token_}({Token.tokenId_}, {Token.tokenName_}, {Token.dnsName_}, 
                                {Token.serverEndPoint_}, {Token.maxTraffic_}, {Token.isPublic_}, {Token.maxClient_}, {Token.endTime_}, {Token.lifeTime_})

                    VALUES (@{nameof(tokenId)}, @{nameof(tokenName)}, @{nameof(dnsName)}, @{nameof(serverEndPoint)}, @{nameof(maxTraffic)}, 0, 
                            @{nameof(maxClient)}, @{nameof(endTime)}, @{nameof(lifetime)} );
            ";

            using var connection = App.OpenConnection();
            await connection.QueryAsync(sql, new { tokenId, tokenName, dnsName, serverEndPoint, maxTraffic, maxClient, endTime, lifetime });
            return FromId(tokenId);
        }

        public async Task<Token> GetToken()
        {
            using var connection = App.OpenConnection();
            var sql = @$"
                SELECT 
                       T.{Token.tokenId_}, 
                       T.{Token.tokenName_},
                       T.{Token.supportId_},
                       T.{Token.secret_},
                       T.{Token.dnsName_},
                       T.{Token.serverEndPoint_},
                       T.{Token.maxClient_},
                       T.{Token.maxTraffic_},
                       T.{Token.lifeTime_},
                       T.{Token.endTime_},
                       T.{Token.startTime_},
                       T.{Token.isPublic_}
                FROM {Token.Token_} AS T
                WHERE T.{Token.tokenId_} = @{nameof(Id)}
                ";

            var ret = await connection.QuerySingleOrDefaultAsync<Token>(sql, new { Id });
            if (ret == null) throw new KeyNotFoundException();
            return ret;
        }

        public async Task<AccessUsage> GetAccessUsage(string clientIp)
        {
            using var connection = App.OpenConnection();
            var sql = @$"
                SELECT 
                       CU.{AccessUsage.sentTraffic_},
                       CU.{AccessUsage.receivedTraffic_},
                       CU.{AccessUsage.totalSentTraffic_},
                       CU.{AccessUsage.totalReceivedTraffic_}
                FROM {Token.Token_} AS T
                    LEFT JOIN {AccessUsage.AccessUsage_} AS CU
                        ON T.{Token.tokenId_} = CU.{AccessUsage.tokenId_} AND (CU.{AccessUsage.clientIp_} IS NULL OR CU.{AccessUsage.clientIp_} = @{nameof(clientIp)})
                WHERE T.{Token.tokenId_} = @{nameof(Id)}
                ";

            var result = await connection.QuerySingleOrDefaultAsync<AccessUsage>(sql, new { Id, clientIp });
            return result;
        }

        public async Task<AccessUsage> AddAccessUsage(string clientIp, long sentTraffic, long receivedTraffic)
        {
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
                    UPDATE  {AccessUsage.AccessUsage_}
                       SET  {AccessUsage.sentTraffic_} = @{AccessUsage.sentTraffic_}, {AccessUsage.receivedTraffic_} = @{AccessUsage.receivedTraffic_}, 
                            {AccessUsage.totalReceivedTraffic_} = @{AccessUsage.totalReceivedTraffic_}, {AccessUsage.totalSentTraffic_} = @{AccessUsage.totalSentTraffic_}
                     WHERE  {AccessUsage.tokenId_} = @{nameof(Id)} AND  {AccessUsage.clientIp_} = @{nameof(clientIp)}
                ";

            using var connection = App.OpenConnection();
            var affectedRecord = await connection.ExecuteAsync(sql, param);

            if (affectedRecord == 0)
            {
                sql = @$"
                    INSERT INTO {AccessUsage.AccessUsage_} ({AccessUsage.tokenId_}, {AccessUsage.clientIp_}, {AccessUsage.sentTraffic_}, 
                                {AccessUsage.receivedTraffic_}, {AccessUsage.totalSentTraffic_}, {AccessUsage.totalReceivedTraffic_})
                    VALUES (@{nameof(Id)}, @{nameof(clientIp)}, @{AccessUsage.sentTraffic_}, @{AccessUsage.receivedTraffic_}, @{AccessUsage.totalReceivedTraffic_}, @{AccessUsage.totalSentTraffic_});
                ";
                await connection.ExecuteAsync(sql, param);
            }

            return accessUsage;
        }
    }
}
