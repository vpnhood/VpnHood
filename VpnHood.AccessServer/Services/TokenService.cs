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

        public ClientInfo GetClientInfo(string clientIp)
        {
            using var connection = App.OpenConnection();
            var sql = @$"
                SELECT 
                       T.{Token.tokenId_} AS '{ClientInfo.token_}.{Token.tokenId_}', 
                       T.{Token.tokenName_} AS '{ClientInfo.token_}.{Token.tokenName_}',
                       T.{Token.supportId_} AS '{ClientInfo.token_}.{Token.supportId_}',
                       T.{Token.secret_} AS '{ClientInfo.token_}.{Token.secret_}',
                       T.{Token.dnsName_} AS '{ClientInfo.token_}.{Token.dnsName_}',
                       T.{Token.serverEndPoint_} AS '{ClientInfo.token_}.{Token.serverEndPoint_}',
                       T.{Token.maxTraffic_} AS '{ClientInfo.token_}.{Token.maxTraffic_}',
                       T.{Token.isPublic_} AS '{ClientInfo.token_}.{Token.isPublic_}',

                       CU.{ClientUsage.sentTraffic_} AS '{ClientInfo.clientUsage_}.{ClientUsage.sentTraffic_}',
                       CU.{ClientUsage.receivedTraffic_} AS '{ClientInfo.clientUsage_}.{ClientUsage.receivedTraffic_}',
                       CU.{ClientUsage.totalSentTraffic_} AS '{ClientInfo.clientUsage_}.{ClientUsage.totalSentTraffic_}',
                       CU.{ClientUsage.totalReceivedTraffic_} AS '{ClientInfo.clientUsage_}.{ClientUsage.totalReceivedTraffic_}'
                FROM {Token.Token_} AS T
                    LEFT JOIN {ClientUsage.ClientUsage_} AS CU
                        ON T.{Token.tokenId_} = CU.{ClientUsage.tokenId_} AND (CU.{ClientUsage.clientIp_} IS NULL OR CU.{ClientUsage.clientIp_} = @{nameof(clientIp)})
                WHERE T.{Token.tokenId_} = @{nameof(Id)}
                FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
                ";

            var result = connection.QuerySingleOrDefault<string>(sql, new { Id, clientIp });
            if (result == null) throw new KeyNotFoundException();
            var ret = JsonSerializer.Deserialize<ClientInfo>(result, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });
            if (ret.clientUsage == null) ret.clientUsage = new ClientUsage();
            return ret;
        }

        public ClientInfo AddClientUsage(string clientIp, long sentTraffic, long receivedTraffic)
        {
            // check cycle first
            PublicCycleService.UpdateCycle();

            // update
            var clientInfo = GetClientInfo(clientIp: clientIp);
            clientInfo.clientUsage.sentTraffic += sentTraffic;
            clientInfo.clientUsage.receivedTraffic += receivedTraffic;
            clientInfo.clientUsage.totalSentTraffic += sentTraffic;
            clientInfo.clientUsage.totalReceivedTraffic += receivedTraffic;
            var param = new
            {
                Id,
                clientIp,
                clientInfo.clientUsage.sentTraffic,
                clientInfo.clientUsage.receivedTraffic,
                clientInfo.clientUsage.totalSentTraffic,
                clientInfo.clientUsage.totalReceivedTraffic
            };

            using var connection = App.OpenConnection();
            var sql = $@"
                    UPDATE  {ClientUsage.ClientUsage_}
                       SET  {ClientUsage.sentTraffic_} = @{ClientUsage.sentTraffic_}, {ClientUsage.receivedTraffic_} = @{ClientUsage.receivedTraffic_}, 
                            {ClientUsage.totalReceivedTraffic_} = @{ClientUsage.totalReceivedTraffic_}, {ClientUsage.totalSentTraffic_} = @{ClientUsage.totalSentTraffic_}
                     WHERE  {ClientUsage.tokenId_} = @{nameof(Id)} AND  {ClientUsage.clientIp_} = @{nameof(clientIp)}
                ";
            var affectedRecord = connection.Execute(sql, param);

            if (affectedRecord == 0)
            {
                sql = @$"
                    INSERT INTO {ClientUsage.ClientUsage_} ({ClientUsage.tokenId_}, {ClientUsage.clientIp_}, {ClientUsage.sentTraffic_}, 
                                {ClientUsage.receivedTraffic_}, {ClientUsage.totalSentTraffic_}, {ClientUsage.totalReceivedTraffic_})
                    VALUES (@{nameof(Id)}, @{nameof(clientIp)}, @{ClientUsage.sentTraffic_}, @{ClientUsage.receivedTraffic_}, @{ClientUsage.totalReceivedTraffic_}, @{ClientUsage.totalSentTraffic_});
                ";
                connection.Execute(sql, param);
            }

            return clientInfo;
        }
    }
}
