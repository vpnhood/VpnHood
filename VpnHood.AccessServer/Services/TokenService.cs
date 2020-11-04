using Dapper;
using System;
using System.Collections.Generic;
using System.Text.Json;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Services
{
    public class TokenService
    {
        public Guid Id { get; private set; }
        public static TokenService FromId(Guid id) => new TokenService() { Id = id };

        public static TokenService CreatePublic(string tokenName, string dnsName, string serverEndPoint, long maxTraffic)
        {
            var tokenId = Guid.NewGuid();
            var sql = @$"
                    INSERT INTO {Token.Token_}({Token.tokenId_}, {Token.tokenName_}, {Token.dnsName_}, {Token.serverEndPoint_}, {Token.maxTraffic_}, {Token.isPublic_})
                    VALUES(@{nameof(tokenId)}, @{nameof(tokenName)}, @{nameof(dnsName)}, @{nameof(serverEndPoint)}, @{nameof(maxTraffic)}, 1);
            ";

            using var connection = App.OpenConnection();
            connection.Query(sql, new { tokenId, tokenName, dnsName, serverEndPoint, maxTraffic });
            return FromId(tokenId);
        }

        public AccessInfo GetClientInfo(string clientIp)
        {
            using var connection = App.OpenConnection();
            var sql = @$"
                SELECT 
                       T.{Token.tokenId_} AS '{AccessInfo.token_}.{Token.tokenId_}', 
                       T.{Token.tokenName_} AS '{AccessInfo.token_}.{Token.tokenName_}',
                       T.{Token.supportId_} AS '{AccessInfo.token_}.{Token.supportId_}',
                       T.{Token.secret_} AS '{AccessInfo.token_}.{Token.secret_}',
                       T.{Token.dnsName_} AS '{AccessInfo.token_}.{Token.dnsName_}',
                       T.{Token.serverEndPoint_} AS '{AccessInfo.token_}.{Token.serverEndPoint_}',
                       T.{Token.maxTraffic_} AS '{AccessInfo.token_}.{Token.maxTraffic_}',
                       T.{Token.isPublic_} AS '{AccessInfo.token_}.{Token.isPublic_}',

                       CU.{AccessUsage.sentTraffic_} AS '{AccessInfo.accessUsage_}.{AccessUsage.sentTraffic_}',
                       CU.{AccessUsage.receivedTraffic_} AS '{AccessInfo.accessUsage_}.{AccessUsage.receivedTraffic_}',
                       CU.{AccessUsage.totalSentTraffic_} AS '{AccessInfo.accessUsage_}.{AccessUsage.totalSentTraffic_}',
                       CU.{AccessUsage.totalReceivedTraffic_} AS '{AccessInfo.accessUsage_}.{AccessUsage.totalReceivedTraffic_}'
                FROM {Token.Token_} AS T
                    LEFT JOIN {AccessUsage.ClientUsage_} AS CU
                        ON T.{Token.tokenId_} = CU.{AccessUsage.tokenId_} AND (CU.{AccessUsage.clientIp_} IS NULL OR CU.{AccessUsage.clientIp_} = @{nameof(clientIp)})
                WHERE T.{Token.tokenId_} = @{nameof(Id)}
                FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
                ";

            var result = connection.QuerySingleOrDefault<string>(sql, new { Id, clientIp });
            if (result == null) throw new KeyNotFoundException();
            var ret = JsonSerializer.Deserialize<AccessInfo>(result, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });
            if (ret.accessUsage == null) ret.accessUsage = new AccessUsage();
            return ret;
        }

        public AccessInfo AddClientUsage(string clientIp, long sentTraffic, long receivedTraffic)
        {
            // check cycle first
            PublicCycleService.UpdateCycle();

            // update
            var clientInfo = GetClientInfo(clientIp: clientIp);
            clientInfo.accessUsage.sentTraffic += sentTraffic;
            clientInfo.accessUsage.receivedTraffic += receivedTraffic;
            clientInfo.accessUsage.totalSentTraffic += sentTraffic;
            clientInfo.accessUsage.totalReceivedTraffic += receivedTraffic;
            var param = new
            {
                Id,
                clientIp,
                clientInfo.accessUsage.sentTraffic,
                clientInfo.accessUsage.receivedTraffic,
                clientInfo.accessUsage.totalSentTraffic,
                clientInfo.accessUsage.totalReceivedTraffic
            };

            using var connection = App.OpenConnection();
            var sql = $@"
                    UPDATE  {AccessUsage.ClientUsage_}
                       SET  {AccessUsage.sentTraffic_} = @{AccessUsage.sentTraffic_}, {AccessUsage.receivedTraffic_} = @{AccessUsage.receivedTraffic_}, 
                            {AccessUsage.totalReceivedTraffic_} = @{AccessUsage.totalReceivedTraffic_}, {AccessUsage.totalSentTraffic_} = @{AccessUsage.totalSentTraffic_}
                     WHERE  {AccessUsage.tokenId_} = @{nameof(Id)} AND  {AccessUsage.clientIp_} = @{nameof(clientIp)}
                ";
            var affectedRecord = connection.Execute(sql, param);

            if (affectedRecord == 0)
            {
                sql = @$"
                    INSERT INTO {AccessUsage.ClientUsage_} ({AccessUsage.tokenId_}, {AccessUsage.clientIp_}, {AccessUsage.sentTraffic_}, 
                                {AccessUsage.receivedTraffic_}, {AccessUsage.totalSentTraffic_}, {AccessUsage.totalReceivedTraffic_})
                    VALUES (@{nameof(Id)}, @{nameof(clientIp)}, @{AccessUsage.sentTraffic_}, @{AccessUsage.receivedTraffic_}, @{AccessUsage.totalReceivedTraffic_}, @{AccessUsage.totalSentTraffic_});
                ";
                connection.Execute(sql, param);
            }

            return clientInfo;
        }
    }
}
