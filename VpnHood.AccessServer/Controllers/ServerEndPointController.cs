using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Transactions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.DTOs;
using VpnHood.AccessServer.Models;

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
            // set 443 default
            var publicEndPointObj = IPEndPoint.Parse(publicEndPoint);
            publicEndPoint = publicEndPointObj.Port != 0 ? publicEndPointObj.ToString() : throw new ArgumentException("Port is not specified!", nameof(publicEndPoint));

            await using VhContext vhContext = new();
            createParams.AccessTokenGroupId ??=
                (await vhContext.AccessTokenGroups.SingleAsync(x => x.ProjectId == projectId && x.IsDefault))
                .AccessTokenGroupId;

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
                PublicEndPoint = publicEndPoint,
                PrivateEndPoint = createParams.PrivateEndPoint?.ToString(),
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
            var serverEndPoint =
                await vhContext.ServerEndPoints.SingleAsync(x =>
                    x.ProjectId == projectId && x.PublicEndPoint == publicEndPoint);

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

            // update privateEndPoint
            if (updateParams.PrivateEndPoint != null)
                serverEndPoint.PrivateEndPoint = updateParams.PrivateEndPoint.ToString();

            vhContext.ServerEndPoints.Update(serverEndPoint);

            await vhContext.SaveChangesAsync();
            trans.Complete();
        }

        [HttpGet("{publicEndPoint}")]
        public async Task<ServerEndPoint> Get(Guid projectId, string publicEndPoint)
        {
            publicEndPoint = AccessUtil.ValidateIpEndPoint(publicEndPoint);
            await using VhContext vhContext = new();
            return await vhContext.ServerEndPoints
                .Include(e => e.AccessTokenGroup)
                .SingleAsync(e =>
                e.ProjectId == projectId && e.AccessTokenGroup != null && e.PublicEndPoint == publicEndPoint);
        }

        [HttpDelete("{publicEndPoint}")]
        public async Task Delete(Guid projectId, string publicEndPoint)
        {
            publicEndPoint = AccessUtil.ValidateIpEndPoint(publicEndPoint);

            await using VhContext vhContext = new();
            var serverEndPoint =
                await vhContext.ServerEndPoints.SingleAsync(x =>
                    x.ProjectId == projectId && x.PublicEndPoint == publicEndPoint);
            if (serverEndPoint.IsDefault)
                throw new InvalidOperationException($"Could not delete default {nameof(ServerEndPoint)}!");

            vhContext.ServerEndPoints.Remove(serverEndPoint);
            await vhContext.SaveChangesAsync();
        }
    }
}