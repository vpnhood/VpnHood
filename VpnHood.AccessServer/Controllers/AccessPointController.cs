using System;
using System.Linq;
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
    [Route("/api/projects/{projectId}/access-points")]
    [Authorize(AuthenticationSchemes = "auth", Roles = "Admin")]
    public class AccessPointController : SuperController<AccessPointController>
    {
        public AccessPointController(ILogger<AccessPointController> logger) : base(logger)
        {
        }

        /// <summary>
        ///     Create a new server endpoint for a server endpoint group
        /// </summary>
        /// <param name="projectId"></param>
        /// <param name="createParams"></param>
        /// <returns></returns>
        [HttpPost]
        public async Task<AccessPoint> Create(Guid projectId, AccessPointCreateParams createParams)
        {
            // set 443 default
            var publicEndPoint = createParams.PublicEndPoint.Port != 0 ? createParams.PublicEndPoint.ToString() : throw new InvalidOperationException($"Port is not specified in {nameof(createParams.PublicEndPoint)}!");

            await using VhContext vhContext = new();
            createParams.AccessPointGroupId ??=
                (await vhContext.AccessPointGroups.SingleAsync(x => x.ProjectId == projectId && x.IsDefault))
                .AccessPointGroupId;

            // remove previous default 
            var prevDefault = vhContext.AccessPoints.FirstOrDefault(x =>
                x.ProjectId == projectId && x.AccessPointGroupId == createParams.AccessPointGroupId && x.IsDefault);
            if (prevDefault != null && createParams.MakeDefault)
            {
                prevDefault.IsDefault = false;
                vhContext.AccessPoints.Update(prevDefault);
            }

            AccessPoint ret = new()
            {
                ProjectId = projectId,
                IsDefault = createParams.MakeDefault || prevDefault == null,
                AccessPointGroupId = createParams.AccessPointGroupId.Value,
                PublicEndPoint = publicEndPoint,
                PrivateEndPoint = createParams.PrivateEndPoint?.ToString(),
                ServerId = null
            };

            await vhContext.AccessPoints.AddAsync(ret);
            await vhContext.SaveChangesAsync();
            return ret;
        }


        [HttpPatch("{publicEndPoint}")]
        public async Task Update(Guid projectId, string publicEndPoint, AccessPointUpdateParams updateParams)
        {
            publicEndPoint = AccessUtil.ValidateIpEndPoint(publicEndPoint);

            await using VhContext vhContext = new();
            var accessPoint =
                await vhContext.AccessPoints.SingleAsync(x =>
                    x.ProjectId == projectId && x.PublicEndPoint == publicEndPoint);

            // check accessPointGroupId permission
            if (updateParams.AccessPointGroupId != null)
            {
                await vhContext.AccessPointGroups.SingleAsync(x =>
                    x.ProjectId == projectId && x.AccessPointGroupId == updateParams.AccessPointGroupId);
                accessPoint.AccessPointGroupId = updateParams.AccessPointGroupId;
            }

            // transaction required for changing default. EF can not do this due the index
            using var trans = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

            // change default
            if (!accessPoint.IsDefault && updateParams.MakeDefault?.Value == true)
            {
                var prevDefault = vhContext.AccessPoints.FirstOrDefault(x =>
                    x.ProjectId == projectId && x.AccessPointGroupId == accessPoint.AccessPointGroupId &&
                    x.IsDefault);
                if (prevDefault != null)
                {
                    prevDefault.IsDefault = false;
                    vhContext.AccessPoints.Update(prevDefault);
                    await vhContext.SaveChangesAsync();
                }

                accessPoint.IsDefault = true;
            }

            // update privateEndPoint
            if (updateParams.PrivateEndPoint != null)
                accessPoint.PrivateEndPoint = updateParams.PrivateEndPoint.ToString();

            vhContext.AccessPoints.Update(accessPoint);

            await vhContext.SaveChangesAsync();
            trans.Complete();
        }

        [HttpGet("{publicEndPoint}")]
        public async Task<AccessPoint> Get(Guid projectId, string publicEndPoint)
        {
            publicEndPoint = AccessUtil.ValidateIpEndPoint(publicEndPoint);
            await using VhContext vhContext = new();
            return await vhContext.AccessPoints
                .Include(e => e.AccessPointGroup)
                .SingleAsync(e =>
                e.ProjectId == projectId && e.AccessPointGroup != null && e.PublicEndPoint == publicEndPoint);
        }

        [HttpDelete("{publicEndPoint}")]
        public async Task Delete(Guid projectId, string publicEndPoint)
        {
            publicEndPoint = AccessUtil.ValidateIpEndPoint(publicEndPoint);

            await using VhContext vhContext = new();
            var accessPoint =
                await vhContext.AccessPoints.SingleAsync(x =>
                    x.ProjectId == projectId && x.PublicEndPoint == publicEndPoint);
            if (accessPoint.IsDefault)
                throw new InvalidOperationException($"Could not delete default {nameof(AccessPoint)}!");

            vhContext.AccessPoints.Remove(accessPoint);
            await vhContext.SaveChangesAsync();
        }
    }
}