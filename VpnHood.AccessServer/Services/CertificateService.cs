using Dapper;
using System;
using System.Collections.Generic;
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
        public static CertificateService FromId(string serverEndPoint) => new CertificateService() { ServerEndPoint = serverEndPoint };

        public static async Task<CertificateService> Create(string serverEndPoint, string subjectName)
        {
            var certificate = CertificateUtil.CreateSelfSigned(subjectName);
            var rawData = certificate.Export(X509ContentType.Pfx);

            using var connection = App.OpenConnection();
            var sql = @$"
                    INSERT INTO {Certificate.Table_} ({Certificate.serverEndPoint_}, {Certificate.rawData_})
                    VALUES (@{nameof(serverEndPoint)}, @{nameof(rawData)})
                ";
            await connection.ExecuteAsync(sql, new { serverEndPoint, rawData });
            return FromId(serverEndPoint);
        }

        public static async Task<CertificateService> Create(string serverEndPoint, byte[] rawData, string password)
        {
            var x509Certificate = new X509Certificate2(rawData, password, X509KeyStorageFlags.Exportable); //validate rawData
            rawData = x509Certificate.Export(X509ContentType.Pfx); //remove password
            
            using var connection = App.OpenConnection();
            var sql = @$"
                    INSERT INTO {Certificate.Table_} ({Certificate.serverEndPoint_}, {Certificate.rawData_})
                    VALUES (@{nameof(serverEndPoint)}, @{nameof(rawData)})
                ";
            await connection.ExecuteAsync(sql, new { serverEndPoint, rawData });
            return FromId(serverEndPoint);
        }

        public async Task Delete()
        {
            using var connection = App.OpenConnection();
            var sql = @$"
                    DELETE  FROM {Certificate.Table_}
                    WHERE  {Certificate.serverEndPoint_} = @{nameof(ServerEndPoint)};
                ";
            var ret = await connection.ExecuteAsync(sql, new { ServerEndPoint });
            if (ret == 0) throw new KeyNotFoundException();
        }

        public async Task<Certificate> Get()
        {
            using var connection = App.OpenConnection();
            var sql = @$"
                SELECT 
                       C.{Certificate.serverEndPoint_}, 
                       C.{Certificate.rawData_}
                FROM {Certificate.Table_} AS C
                WHERE C.{Certificate.serverEndPoint_} = @{nameof(ServerEndPoint)}
                ";
            var ret = await connection.QuerySingleOrDefaultAsync<Certificate>(sql, new { ServerEndPoint });
            if (ret == null) throw new KeyNotFoundException();
            return ret;
        }
    }
}
