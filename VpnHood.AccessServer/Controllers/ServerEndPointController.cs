using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Data.SqlClient;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using VpnHood.AccessServer.Models;
using VpnHood.Server;

namespace VpnHood.AccessServer.Controllers
{
    [ApiController]
    [Route("[controller]")]
    [Authorize(AuthenticationSchemes = "auth", Roles = "Admin")]
    public class ServerEndPointController : SuperController<ServerEndPointController>
    {

        public ServerEndPointController(ILogger<ServerEndPointController> logger) : base(logger)
        {
        }

        [HttpPost]
        [Route(nameof(CreateFromCertificate))]
        public async Task<ServerEndPoint> CreateFromCertificate(Guid accountId, string publicEndPoint, byte[] certificateRawData, Guid? accessTokenGroupId = null, string password = null, bool isDefault = false, bool overwrite = false)
        {
            publicEndPoint = AccessUtil.ValidateIpEndPoint(publicEndPoint);

            if (string.IsNullOrEmpty(publicEndPoint)) throw new ArgumentNullException(nameof(publicEndPoint));
            if (certificateRawData == null || certificateRawData.Length == 0) throw new ArgumentNullException(nameof(certificateRawData));
            publicEndPoint = IPEndPoint.Parse(publicEndPoint).ToString(); //validate IPEndPoint
            X509Certificate2 x509Certificate2 = new(certificateRawData, password, X509KeyStorageFlags.Exportable);

            using VhContext vhContext = new();
            if (accessTokenGroupId == null)
                accessTokenGroupId = (await vhContext.AccessTokenGroups.SingleAsync(x => x.AccountId == accountId && x.IsDefault)).AccessTokenGroupId;

            ServerEndPoint ret = new() {
                AccountId = accountId,
                IsDefault = isDefault,
                AccessTokenGroupId = accessTokenGroupId.Value,
                PulicEndPoint = publicEndPoint,
                CertificateRawData = x509Certificate2.Export(X509ContentType.Pfx), //removing password
                ServerId = null,
            };

            await vhContext.ServerEndPoints.AddAsync(ret);
            await vhContext.SaveChangesAsync();
            return ret;
        }

        [HttpPost]
        [Route(nameof(Create))]
        public Task<ServerEndPoint> Create(Guid accountId, string publicEndPoint, Guid? accessTokenGroupId = null, string subjectName = null, bool isDefault = false)
        {
            var certificate = CertificateUtil.CreateSelfSigned(subjectName);
            var certificateRawData = certificate.Export(X509ContentType.Pfx);
            publicEndPoint = IPEndPoint.Parse(publicEndPoint).ToString();
            return CreateFromCertificate(accountId, publicEndPoint, accessTokenGroupId : accessTokenGroupId, 
                certificateRawData : certificateRawData, 
                password: null, isDefault: isDefault);
        }

        [HttpGet]
        [Route(nameof(Get))]
        public Task<ServerEndPoint> Get(Guid accountId, string publicEndPoint)
        {
            publicEndPoint = AccessUtil.ValidateIpEndPoint(publicEndPoint);
            using VhContext vhContext = new();
            return vhContext.ServerEndPoints.SingleAsync(e => e.AccountId == accountId && e.PulicEndPoint== publicEndPoint);
        }

        [HttpDelete]
        [Route(nameof(Delete))]
        public async Task Delete(Guid accountId, string publicEndPoint)
        {
            publicEndPoint = AccessUtil.ValidateIpEndPoint(publicEndPoint);

            using VhContext vhContext = new();
            ServerEndPoint serverEndPointEntity = await vhContext.ServerEndPoints.SingleAsync(x => x.AccountId == accountId && x.PulicEndPoint == publicEndPoint);
            if (serverEndPointEntity.IsDefault)
                throw new InvalidOperationException($"Could not delete default {nameof(ServerEndPoint)}!");

            vhContext.ServerEndPoints.Remove(serverEndPointEntity);
            await vhContext.SaveChangesAsync();
        }
    }
}
