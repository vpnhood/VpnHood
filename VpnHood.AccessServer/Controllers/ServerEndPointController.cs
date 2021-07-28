using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Transactions;
using VpnHood.AccessServer.Exceptions;
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
        [Route(nameof(Create))]
        public Task<ServerEndPoint> Create(Guid accountId, string publicEndPoint, Guid? accessTokenGroupId = null, 
            string subjectName = null, bool makeDefault = false)
        {
            var certificate = CertificateUtil.CreateSelfSigned(subjectName);
            var certificateRawData = certificate.Export(X509ContentType.Pfx);
            publicEndPoint = IPEndPoint.Parse(publicEndPoint).ToString();
            return CreateFromCertificate(accountId, publicEndPoint, accessTokenGroupId: accessTokenGroupId,
                certificateRawData: certificateRawData,
                password: null, makeDefault: makeDefault);
        }

        [HttpPost]
        [Route(nameof(CreateFromCertificate))]
        public async Task<ServerEndPoint> CreateFromCertificate(Guid accountId, string publicEndPoint, byte[] certificateRawData, Guid? accessTokenGroupId = null, 
            string password = null, bool makeDefault = false)
        {
            publicEndPoint = AccessUtil.ValidateIpEndPoint(publicEndPoint);

            if (string.IsNullOrEmpty(publicEndPoint)) throw new ArgumentNullException(nameof(publicEndPoint));
            if (certificateRawData == null || certificateRawData.Length == 0) throw new ArgumentNullException(nameof(certificateRawData));
            publicEndPoint = IPEndPoint.Parse(publicEndPoint).ToString(); //validate IPEndPoint
            X509Certificate2 x509Certificate2 = new(certificateRawData, password, X509KeyStorageFlags.Exportable);

            using VhContext vhContext = new();
            if (accessTokenGroupId == null)
                accessTokenGroupId = (await vhContext.AccessTokenGroups.SingleAsync(x => x.AccountId == accountId && x.IsDefault)).AccessTokenGroupId;

            // make sure publicEndPoint does not exist
            if (await vhContext.ServerEndPoints.AnyAsync(x => x.AccountId == accountId && x.PulicEndPoint == publicEndPoint))
                throw new AlreadyExistsException(nameof(VhContext.ServerEndPoints));

            // remove previous default 
            var oldDefaultServerEndPoint = vhContext.ServerEndPoints.FirstOrDefault(x => x.AccountId == accountId && x.AccessTokenGroupId == accessTokenGroupId && x.IsDefault);
            if (oldDefaultServerEndPoint != null && makeDefault)
            {
                oldDefaultServerEndPoint.IsDefault = false;
                vhContext.ServerEndPoints.Update(oldDefaultServerEndPoint);
            }

            ServerEndPoint ret = new()
            {
                AccountId = accountId,
                IsDefault = makeDefault || oldDefaultServerEndPoint == null,
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
        [Route(nameof(Update))]
        public async Task Update(Guid accountId, string publicEndPoint, Guid? accessTokenGroupId = null, byte[] certificateRawData = null, string password = null, bool makeDefault = false)
        {
            publicEndPoint = AccessUtil.ValidateIpEndPoint(publicEndPoint);

            using VhContext vhContext = new();
            ServerEndPoint serverEndPoint = await vhContext.ServerEndPoints.SingleAsync(x => x.AccountId == accountId && x.PulicEndPoint == publicEndPoint);

            // check accessTokenGroupId permission
            if (accessTokenGroupId.HasValue)
            {
                await vhContext.AccessTokenGroups.SingleAsync(x => x.AccountId == accountId && x.AccessTokenGroupId == accessTokenGroupId);
                serverEndPoint.AccessTokenGroupId = accessTokenGroupId.Value;
            }

            // transaction required for change default. ef can not do this due the index
            using var trans = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

            // change default
            if (!serverEndPoint.IsDefault && makeDefault)
            {
                var oldDefaultServerEndPoint = vhContext.ServerEndPoints.FirstOrDefault(x => x.AccountId == accountId && x.AccessTokenGroupId == accessTokenGroupId && x.IsDefault);
                oldDefaultServerEndPoint.IsDefault = false;
                vhContext.ServerEndPoints.Update(oldDefaultServerEndPoint);
                await vhContext.SaveChangesAsync();
                
                serverEndPoint.IsDefault = true;
            }
            
            // certificate
            if (certificateRawData != null)
            {
                X509Certificate2 x509Certificate2 = new(certificateRawData, password, X509KeyStorageFlags.Exportable);
                serverEndPoint.CertificateRawData = x509Certificate2.Export(X509ContentType.Pfx);
            }
            vhContext.ServerEndPoints.Update(serverEndPoint);
            
            await vhContext.SaveChangesAsync();
            trans.Complete();

        }

        [HttpGet]
        [Route(nameof(Get))]
        public async Task<ServerEndPoint> Get(Guid accountId, string publicEndPoint)
        {
            publicEndPoint = AccessUtil.ValidateIpEndPoint(publicEndPoint);
            using VhContext vhContext = new();
            return await vhContext.ServerEndPoints.SingleAsync(e => e.AccountId == accountId && e.PulicEndPoint == publicEndPoint);
        }

        [HttpDelete]
        [Route(nameof(Delete))]
        public async Task Delete(Guid accountId, string publicEndPoint)
        {
            publicEndPoint = AccessUtil.ValidateIpEndPoint(publicEndPoint);

            using VhContext vhContext = new();
            ServerEndPoint serverEndPoint = await vhContext.ServerEndPoints.SingleAsync(x => x.AccountId == accountId && x.PulicEndPoint == publicEndPoint);
            if (serverEndPoint.IsDefault)
                throw new InvalidOperationException($"Could not delete default {nameof(ServerEndPoint)}!");

            vhContext.ServerEndPoints.Remove(serverEndPoint);
            await vhContext.SaveChangesAsync();
        }
    }
}
