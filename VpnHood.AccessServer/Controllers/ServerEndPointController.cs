using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Transactions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.DTOs;
using VpnHood.AccessServer.Exceptions;
using VpnHood.AccessServer.Models;
using VpnHood.Server;

namespace VpnHood.AccessServer.Controllers
{
    [Route("/api/projects/{projectId}/server-endpoints")]
    [Authorize(AuthenticationSchemes = "auth", Roles = "Admin")]
    public class ServerEndPointController : SuperController<ServerEndPointController>
    {
        public ServerEndPointController(ILogger<ServerEndPointController> logger) : base(logger)
        {
        }

        /// <summary>
        ///     Create a new server endpoint for a server endpoint group
        /// </summary>
        /// <param name="projectId"></param>
        /// <param name="publicEndPoint">sample: 1.100.101.102:443</param>
        /// <param name="createParams"></param>
        /// <returns></returns>
        [HttpPost("{publicEndPoint}")]
        public async Task<ServerEndPoint> Create(Guid projectId, string publicEndPoint,
            ServerEndPointCreateParams createParams)
        {
            publicEndPoint = AccessUtil.ValidateIpEndPoint(publicEndPoint);
            if (!string.IsNullOrEmpty(createParams.SubjectName) && createParams.CertificateRawData?.Length > 0)
                throw new InvalidOperationException(
                    $"Could not set both {createParams.SubjectName} and {createParams.CertificateRawData} together!");

            // create cert
            var certificateRawBuffer = createParams.CertificateRawData?.Length > 0
                ? createParams.CertificateRawData
                : CertificateUtil.CreateSelfSigned(createParams.SubjectName).Export(X509ContentType.Pfx);

            // add cert into 
            X509Certificate2 x509Certificate2 = new(certificateRawBuffer, createParams.CertificatePassword,
                X509KeyStorageFlags.Exportable);
            certificateRawBuffer = x509Certificate2.Export(X509ContentType.Pfx); //removing password

            await using VhContext vhContext = new();
            if (createParams.AccessTokenGroupId == null)
                createParams.AccessTokenGroupId =
                    (await vhContext.AccessTokenGroups.SingleAsync(x => x.ProjectId == projectId && x.IsDefault))
                    .AccessTokenGroupId;

            // make sure publicEndPoint does not exist
            if (await vhContext.ServerEndPoints.AnyAsync(x =>
                x.ProjectId == projectId && x.PulicEndPoint == publicEndPoint))
                throw new AlreadyExistsException(nameof(VhContext.ServerEndPoints));

            // remove previous default 
            var prevDefault = vhContext.ServerEndPoints.FirstOrDefault(x =>
                x.ProjectId == projectId && x.AccessTokenGroupId == createParams.AccessTokenGroupId && x.IsDefault);
            if (prevDefault != null && createParams.MakeDefault)
            {
                prevDefault.IsDefault = false;
                vhContext.ServerEndPoints.Update(prevDefault);
            }

            ServerEndPoint ret = new()
            {
                ProjectId = projectId,
                IsDefault = createParams.MakeDefault || prevDefault == null,
                AccessTokenGroupId = createParams.AccessTokenGroupId.Value,
                PulicEndPoint = publicEndPoint,
                CertificateRawData = certificateRawBuffer,
                CertificateCommonName = x509Certificate2.GetNameInfo(X509NameType.DnsName, false),
                ServerId = null
            };

            await vhContext.ServerEndPoints.AddAsync(ret);
            await vhContext.SaveChangesAsync();
            return ret;
        }


        [HttpPut("{publicEndPoint}")]
        public async Task Update(Guid projectId, string publicEndPoint, ServerEndPointUpdateParams updateParams)
        {
            publicEndPoint = AccessUtil.ValidateIpEndPoint(publicEndPoint);

            await using VhContext vhContext = new();
            ServerEndPoint serverEndPoint =
                await vhContext.ServerEndPoints.SingleAsync(x =>
                    x.ProjectId == projectId && x.PulicEndPoint == publicEndPoint);

            // check accessTokenGroupId permission
            if (updateParams.AccessTokenGroupId != null)
            {
                await vhContext.AccessTokenGroups.SingleAsync(x =>
                    x.ProjectId == projectId && x.AccessTokenGroupId == updateParams.AccessTokenGroupId);
                serverEndPoint.AccessTokenGroupId = updateParams.AccessTokenGroupId;
            }

            // transaction required for changing default. EF can not do this due the index
            using var trans = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

            // change default
            if (!serverEndPoint.IsDefault && updateParams.MakeDefault?.Value == true)
            {
                var prevDefault = vhContext.ServerEndPoints.FirstOrDefault(x =>
                    x.ProjectId == projectId && x.AccessTokenGroupId == serverEndPoint.AccessTokenGroupId &&
                    x.IsDefault);
                if (prevDefault != null)
                {
                    prevDefault.IsDefault = false;
                    vhContext.ServerEndPoints.Update(prevDefault);
                    await vhContext.SaveChangesAsync();
                }

                serverEndPoint.IsDefault = true;
            }

            // certificate
            if (updateParams.CertificateRawData != null)
            {
                X509Certificate2 x509Certificate2 = new(updateParams.CertificateRawData,
                    updateParams.CertificatePassword?.Value, X509KeyStorageFlags.Exportable);
                serverEndPoint.CertificateCommonName = x509Certificate2.GetNameInfo(X509NameType.DnsName, false);
                serverEndPoint.CertificateRawData = x509Certificate2.Export(X509ContentType.Pfx);
            }

            vhContext.ServerEndPoints.Update(serverEndPoint);

            await vhContext.SaveChangesAsync();
            trans.Complete();
        }

        [HttpGet("{publicEndPoint}")]
        public async Task<ServerEndPoint> Get(Guid projectId, string publicEndPoint)
        {
            publicEndPoint = AccessUtil.ValidateIpEndPoint(publicEndPoint);
            await using VhContext vhContext = new();
            return await vhContext.ServerEndPoints.SingleAsync(e =>
                e.ProjectId == projectId && e.PulicEndPoint == publicEndPoint);
        }

        [HttpDelete("{publicEndPoint}")]
        public async Task Delete(Guid projectId, string publicEndPoint)
        {
            publicEndPoint = AccessUtil.ValidateIpEndPoint(publicEndPoint);

            await using VhContext vhContext = new();
            ServerEndPoint serverEndPoint =
                await vhContext.ServerEndPoints.SingleAsync(x =>
                    x.ProjectId == projectId && x.PulicEndPoint == publicEndPoint);
            if (serverEndPoint.IsDefault)
                throw new InvalidOperationException($"Could not delete default {nameof(ServerEndPoint)}!");

            vhContext.ServerEndPoints.Remove(serverEndPoint);
            await vhContext.SaveChangesAsync();
        }
    }
}