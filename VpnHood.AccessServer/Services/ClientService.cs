using Dapper;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Services
{
    public class ClientService
    {
        public Guid Id { get; private set; }
        public static ClientService FromId(Guid id) => new() { Id = id };

        public static async Task<ClientService> Create(Guid clientId, string userAgent)
        {
            var sql = @$"
                    INSERT INTO {Client.Table_} ({Client.clientId_}, {Client.userAgent_})
                    VALUES (@{nameof(clientId)}, @{nameof(userAgent)});
            ";

            using var sqlConnection = App.OpenConnection();
            await sqlConnection.QueryAsync(sql, new { clientId, userAgent });
            return FromId(clientId);
        }

        public async Task<Client> Get()
        {
            var sql = @$"
                SELECT 
                       C.{Client.clientId_}, 
                       C.{Client.userAgent_},
                       C.{Client.createdTime_}
                FROM {Client.Table_} AS C
                WHERE C.{Client.clientId_} = @{nameof(Id)}
                ";

            using var sqlConnection = App.OpenConnection();
            var ret = await sqlConnection.QuerySingleOrDefaultAsync<Client>(sql, new { Id });
            if (ret == null) throw new KeyNotFoundException();
            return ret;
        }
    }
}
