using Dapper;
using System.Threading.Tasks;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Services
{
    public class ServerEndPointGroupService
    {
        public int Id { get; private set; }
        private ServerEndPointGroupService(int id) => Id = id;
        public static ServerEndPointGroupService FromId(int id) => new(id);

        public static async Task<ServerEndPointGroupService> Create(int serverEndPointGroupId)
        {
            var param = new
            {
                serverEndPointGroupId
            };

            var sql = @$"
                    INSERT INTO {ServerEndPointGroup.Table_} ({ServerEndPointGroup.serverEndPointGroupId_})
                    VALUES (@{nameof(param.serverEndPointGroupId)});
            ";

            using var sqlConnection = App.OpenConnection();
            await sqlConnection.QueryAsync(sql, param);
            return FromId(serverEndPointGroupId);
        }

        public async Task<ServerEndPointGroup> Get()
        {
            var param = new
            {
                Id
            };

            var sql = @$"
                SELECT 
                       C.{ServerEndPointGroup.serverEndPointGroupId_}, 
                       C.{ServerEndPointGroup.defaultServerEndPoint_}
                FROM {ServerEndPointGroup.Table_} AS C
                WHERE C.{ServerEndPointGroup.serverEndPointGroupId_} = @{nameof(param.Id)}
                ";

            using var sqlConnection = App.OpenConnection();
            var ret = await sqlConnection.QuerySingleAsync<ServerEndPointGroup>(sql, param);
            return ret;
        }

        public async Task<bool> Update(string defaultServerEndPoint)
        {
            var param = new
            {
                Id,
                defaultServerEndPoint
            };

            var sql = @$"
                    UPDATE  {ServerEndPointGroup.Table_}
                       SET  {ServerEndPointGroup.defaultServerEndPoint_} = @{nameof(param.defaultServerEndPoint)}
                     WHERE  {ServerEndPointGroup.serverEndPointGroupId_} = @{nameof(param.Id)};
                ";

            using var sqlConnection = App.OpenConnection();
            var ret = await sqlConnection.ExecuteAsync(sql, param);
            return ret == 1;
        }
    }
}
