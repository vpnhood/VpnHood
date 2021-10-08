using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using System.Transactions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.DTOs;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Security;

namespace VpnHood.AccessServer.Controllers
{
    [Route("/api/projects/{projectId:guid}")]
    public class AccessPointController : SuperController<AccessPointController>
    {
        public AccessPointController(ILogger<AccessPointController> logger) : base(logger)
        {
        }

        [HttpPost("servers/{serverId:guid}/access-points")]
        public async Task<AccessPoint> Create(Guid projectId, Guid serverId, AccessPointCreateParams createParams)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.AccessPointWrite);

            // find default AccessPointGroup
            var accessPointGroup = createParams.AccessPointGroupId != null
                ? await vhContext.AccessPointGroups.SingleAsync(x => x.ProjectId == projectId && x.AccessPointGroupId == createParams.AccessPointGroupId)
                : await vhContext.AccessPointGroups.SingleAsync(x => x.ProjectId == projectId && x.IsDefault);

            // validate serverId project ownership
            var server = await vhContext.Servers.SingleAsync(x => x.ProjectId == projectId && x.ServerId == serverId);

            var ret = new AccessPoint
            {
                ProjectId = projectId,
                ServerId = server.ServerId,
                IncludeInAccessToken = createParams.IncludeInAccessToken,
                AccessPointGroupId = accessPointGroup.AccessPointGroupId,
                PrivateIpAddress = createParams.PrivateIpAddress?.ToString() ?? createParams.PublicIpAddress.ToString(),
                PublicIpAddress = createParams.PublicIpAddress.ToString(),
                TcpPort = createParams.TcpPort,
                UdpPort = createParams.UdpPort
            };

            await vhContext.AccessPoints.AddAsync(ret);
            await vhContext.SaveChangesAsync();
            return ret;
        }

        [HttpGet("access-points/{accessPointId:guid}")]
        public async Task<AccessPoint> Get(Guid projectId, Guid accessPointId)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.AccessPointRead);

            var accessPoint = await vhContext.AccessPoints
                .Include(e => e.AccessPointGroup)
                .SingleAsync(e => e.ProjectId == projectId && e.AccessPointGroup != null && e.AccessPointId == accessPointId);

            // check access
            return accessPoint;
        }

        [HttpPatch("access-points/{accessPointId:guid}")]
        public async Task Update(Guid projectId, Guid accessPointId, AccessPointUpdateParams updateParams)
        {
            if (updateParams.PrivateIpAddress!=null) AccessUtil.ValidateIpEndPoint(updateParams.PrivateIpAddress);
            if (updateParams.PublicIpAddress!=null) AccessUtil.ValidateIpEndPoint(updateParams.PublicIpAddress);

            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.AccessPointWrite);

            // get previous object
            var accessPoint = await vhContext.AccessPoints.SingleAsync(x => x.AccessPointId == accessPointId );

            // update
            if (updateParams.PublicIpAddress != null) accessPoint.PublicIpAddress = updateParams.PublicIpAddress;
            if (updateParams.PrivateIpAddress != null) accessPoint.PrivateIpAddress = updateParams.PrivateIpAddress;
            if (updateParams.TcpPort != null) accessPoint.TcpPort = updateParams.TcpPort;
            if (updateParams.UdpPort != null) accessPoint.UdpPort = updateParams.UdpPort;
            if (updateParams.IncludeInAccessToken != null) accessPoint.IncludeInAccessToken = updateParams.IncludeInAccessToken;

            // update AccessPointGroupId if it is belong to project
            if (updateParams.AccessPointGroupId != null)
            {
                var accessPointGroup = await vhContext.AccessPointGroups.SingleAsync(x => x.ProjectId == projectId && x.AccessPointGroupId == updateParams.AccessPointGroupId);
                accessPoint.AccessPointGroupId = accessPointGroup.AccessPointGroupId;
            }

            vhContext.AccessPoints.Update(accessPoint);
            await vhContext.SaveChangesAsync();
        }

        [HttpDelete("{publicEndPoint}")]
        public async Task Delete(Guid projectId, Guid accessPointId)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.AccessPointWrite);

            var accessPoint =
                await vhContext.AccessPoints
                    .SingleAsync(x =>
                    x.ProjectId == projectId && x.AccessPointId == accessPointId);

            vhContext.AccessPoints.Remove(accessPoint);
            await vhContext.SaveChangesAsync();
        }
    }
}