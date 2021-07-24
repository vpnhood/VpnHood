using Dapper;
using System;
using System.Threading.Tasks;

namespace VpnHood.AccessServer.Services
{
    public class ServerService
    {
        public Guid serverId { get; private set; }
        private ServerService(Guid serverId) => this.serverId = serverId;
        public static ServerService FromId(Guid serverId) => new(serverId);

        public static async Task<ServerService> Create(Guid serverId, string serverName)
        {
            var sql = @$"
                    INSERT INTO {Models.Server.Table_} ({Models.Server.serverId_}, {Models.Server.serverName_})
                    VALUES (@{nameof(serverId)}, @{nameof(serverName)});
            ";

            using var sqlConnection = App.OpenConnection();
            await sqlConnection.QuerySingleAsync(sql, new { serverId, serverName });
            return FromId(serverId);
        }

        public async Task<Models.Server> Get()
        {
            var sql = @$"
                SELECT 
                       C.{Models.Server.serverId_}, 
                       C.{Models.Server.serverName_}, 
                       C.{Models.Server.createdTime_}
                       C.{Models.Server.lastStatusTime_}
                       C.{Models.Server.lastSessionCount_}
                FROM {Models.Server.Table_} AS C
                WHERE C.{Models.Server.serverId_} = @{nameof(serverId)}
                ";

            using var sqlConnection = App.OpenConnection();
            var ret = await sqlConnection.QuerySingleAsync<Models.Server>(sql, new { serverId });
            return ret;
        }

        public async Task<bool> Update(DateTime lastSessionCount)
        {
            var sql = @$"
                    UPDATE  {Models.Server.Table_}
                       SET  ({Models.Server.lastStatusTime_} = getdate(), {Models.Server.lastSessionCount_} = @{nameof(lastSessionCount)})
                     WHERE {Models.Server.serverId_} = @{nameof(serverId)};
                ";

            using var sqlConnection = App.OpenConnection();
            var ret = await sqlConnection.ExecuteAsync(sql, new { serverId, lastSessionCount});
            return ret == 1;
        }
    }
}
