using Dapper;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using VpnHood.AccessServer.Models;
using VpnHood.Server;

namespace VpnHood.AccessServer.Services
{
    public class CertificateService
    {
        public string ServerEndPoint { get; private set; }
        public static CertificateService FromId(string serverEndPoint) => new CertificateService() { ServerEndPoint = IPEndPoint.Parse(serverEndPoint).ToString() };

        public static async Task<CertificateService> Create(string serverEndPoint, string subjectName)
        {
            if (string.IsNullOrEmpty(serverEndPoint)) throw new ArgumentNullException(nameof(serverEndPoint));
            serverEndPoint = IPEndPoint.Parse(serverEndPoint).ToString(); // fix & check serverEndPoint

            var certificate = CertificateUtil.CreateSelfSigned(subjectName);
            var rawData = certificate.Export(X509ContentType.Pfx);

            var sql = @$"
                    INSERT INTO {Certificate.Table_} ({Certificate.serverEndPoint_}, {Certificate.rawData_})
                    VALUES (@{nameof(serverEndPoint)}, @{nameof(rawData)})
                ";

            using var sqlConnection = App.OpenConnection();
            await sqlConnection.ExecuteAsync(sql, new { serverEndPoint, rawData });
            return FromId(serverEndPoint);
        }

        public static async Task<CertificateService> Create(string serverEndPoint, byte[] rawData, string password)
        {
            if (string.IsNullOrEmpty(serverEndPoint)) throw new ArgumentNullException(nameof(serverEndPoint));
            serverEndPoint = IPEndPoint.Parse(serverEndPoint).ToString(); // fix & check serverEndPoint

            if (string.IsNullOrEmpty(serverEndPoint)) throw new ArgumentNullException(nameof(serverEndPoint));
            if (rawData == null || rawData.Length == 0) throw new ArgumentNullException(nameof(rawData));

            var x509Certificate = new X509Certificate2(rawData, password, X509KeyStorageFlags.Exportable); //validate rawData
            rawData = x509Certificate.Export(X509ContentType.Pfx); //remove password

            var sql = @$"
                    INSERT INTO {Certificate.Table_} ({Certificate.serverEndPoint_}, {Certificate.rawData_})
                    VALUES (@{nameof(serverEndPoint)}, @{nameof(rawData)})
                ";
            
            using var sqlConnection = App.OpenConnection();
            await sqlConnection.ExecuteAsync(sql, new { serverEndPoint, rawData });
            return FromId(serverEndPoint);
        }

        public async Task Delete()
        {
            var sql = @$"
                    DELETE  FROM {Certificate.Table_}
                    WHERE  {Certificate.serverEndPoint_} = @{nameof(ServerEndPoint)};
                ";
            
            using var sqlConnection = App.OpenConnection();
            var ret = await sqlConnection.ExecuteAsync(sql, new { ServerEndPoint });
            if (ret == 0) throw new KeyNotFoundException();
        }

        public async Task<Certificate> Get()
        {
            var sql = @$"
                SELECT 
                       C.{Certificate.serverEndPoint_}, 
                       C.{Certificate.rawData_}
                FROM {Certificate.Table_} AS C
                WHERE C.{Certificate.serverEndPoint_} = @{nameof(ServerEndPoint)}
                ";
            
            using var sqlConnection = App.OpenConnection();
            var ret = await sqlConnection.QuerySingleOrDefaultAsync<Certificate>(sql, new { ServerEndPoint });
            if (ret == null) throw new KeyNotFoundException();
            return ret;
        }
    }
}
