using Dapper;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using VpnHood.AccessServer.Models;
using VpnHood.Server;

namespace VpnHood.AccessServer.Services
{
    public class ServerEndPointService
    {
        public string ServerEndPoint { get; private set; }
        public static ServerEndPointService FromId(string serverEndPoint) => new() { ServerEndPoint = serverEndPoint };

        public static Task<ServerEndPointService> Create(string serverEndPoint, string subjectName, bool isPublic)
        {
            if (subjectName is null) throw new ArgumentNullException(nameof(subjectName));
            var certificate = CertificateUtil.CreateSelfSigned(subjectName);
            var rawData = certificate.Export(X509ContentType.Pfx);
            return Create(serverEndPoint, isPublic, rawData, null, false);
        }

        public static async Task<ServerEndPointService> Create(string serverEndPoint, bool isPublic, byte[] rawData, string password, bool overwrite)
        {
            if (string.IsNullOrEmpty(serverEndPoint)) throw new ArgumentNullException(nameof(serverEndPoint));
            if (rawData == null || rawData.Length == 0) throw new ArgumentNullException(nameof(rawData));
            serverEndPoint = IPEndPoint.Parse(serverEndPoint).ToString();

            var x509Certificate = new X509Certificate2(rawData, password, X509KeyStorageFlags.Exportable); //validate rawData

            var param = new
            {
                serverEndPoint,
                isPublic,
                rawData = x509Certificate.Export(X509ContentType.Pfx) //remove password
            };

            var sql = @$"
                    INSERT INTO {Models.ServerEndPoint.Table_} ({Models.ServerEndPoint.serverEndPointId_}, {Models.ServerEndPoint.isPublic_}, {Models.ServerEndPoint.rawData_})
                    VALUES (@{nameof(param.serverEndPoint)}, @{nameof(param.isPublic)}, @{nameof(param.rawData)})
                ";

            using var sqlConnection = App.OpenConnection();
            try
            {
                // insert new
                await sqlConnection.ExecuteAsync(sql, param);
            }
            // update for overwrite
            catch (SqlException ex) when (ex.Number == 2627 && overwrite)
            {
                sql = @$"
                    UPDATE  {Models.ServerEndPoint.Table_}
                        SET  {Models.ServerEndPoint.rawData_} = @{nameof(rawData)}
                        WHERE  {Models.ServerEndPoint.serverEndPointId_} = @{nameof(serverEndPoint)};
                ";
                await sqlConnection.ExecuteAsync(sql, new { serverEndPoint, rawData });
            }

            return FromId(serverEndPoint);
        }

        public async Task Delete()
        {
            var sql = @$"
                    DELETE  FROM {Models.ServerEndPoint.Table_}
                    WHERE  {Models.ServerEndPoint.serverEndPointId_} = @{nameof(ServerEndPoint)};
                ";

            using var sqlConnection = App.OpenConnection();
            var ret = await sqlConnection.ExecuteAsync(sql, new { ServerEndPoint });
            if (ret == 0) throw new KeyNotFoundException();
        }

        public async Task<ServerEndPoint> Get()
        {
            var sql = @$"
                SELECT 
                       C.{Models.ServerEndPoint.serverEndPointId_}, 
                       C.{Models.ServerEndPoint.rawData_}
                FROM {Models.ServerEndPoint.Table_} AS C
                WHERE C.{Models.ServerEndPoint.serverEndPointId_} = @{nameof(ServerEndPoint)}
                ";

            using var sqlConnection = App.OpenConnection();
            var ret = await sqlConnection.QuerySingleAsync<ServerEndPoint>(sql, new { ServerEndPoint });
            return ret;
        }
    }
}
