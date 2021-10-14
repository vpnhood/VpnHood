using System;
using System.Linq;
using System.Threading.Tasks;
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
            var accessPointGroup = await vhContext.AccessPointGroups
                    .SingleAsync(x => x.ProjectId == projectId && x.AccessPointGroupId == createParams.AccessPointGroupId);

            // validate serverId project ownership
            var server = await vhContext.Servers.SingleAsync(x => x.ProjectId == projectId && x.ServerId == serverId);

            var ret = new AccessPoint
            {
                ServerId = server.ServerId,
                AccessPointMode = createParams.AccessPointMode,
                AccessPointGroupId = accessPointGroup.AccessPointGroupId,
                IpAddress = createParams.IpAddress.ToString(),
                TcpPort = createParams.TcpPort,
                UdpPort = createParams.UdpPort,
                IsListen = createParams.IsListen
            };

            await vhContext.AccessPoints.AddAsync(ret);
            await vhContext.SaveChangesAsync();
            return ret;
        }

        [HttpGet("servers/{serverId:guid}/access-points")]
        public async Task<AccessPoint[]> List(Guid projectId, Guid serverId)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.AccessPointRead);

            var ret = await vhContext.AccessPoints
                .Where(x => x.Server!.ProjectId == projectId && x.ServerId == serverId)
                .ToArrayAsync();

            return ret;
        }

        [HttpGet("access-points/{accessPointId:guid}")]
        public async Task<AccessPoint> Get(Guid projectId, Guid accessPointId)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.AccessPointRead);

            var accessPoint = await vhContext.AccessPoints
                .Include(e => e.Server)
                .Include(e => e.AccessPointGroup)
                .SingleAsync(e => e.Server!.ProjectId == projectId && e.AccessPointGroup != null && e.AccessPointId == accessPointId);

            // check access
            return accessPoint;
        }

        [HttpPatch("access-points/{accessPointId:guid}")]
        public async Task Update(Guid projectId, Guid accessPointId, AccessPointUpdateParams updateParams)
        {
            if (updateParams.IpAddress!=null) AccessUtil.ValidateIpEndPoint(updateParams.IpAddress);

            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.AccessPointWrite);

            // get previous object
            var accessPoint = await vhContext.AccessPoints.SingleAsync(x => x.AccessPointId == accessPointId );

            // update
            if (updateParams.IpAddress != null) accessPoint.IpAddress = updateParams.IpAddress;
            if (updateParams.TcpPort != null) accessPoint.TcpPort = updateParams.TcpPort;
            if (updateParams.UdpPort != null) accessPoint.UdpPort = updateParams.UdpPort;
            if (updateParams.AccessPointMode != null) accessPoint.AccessPointMode = updateParams.AccessPointMode;
            if (updateParams.IsListen != null) accessPoint.IsListen = updateParams.IsListen;

            // update AccessPointGroupId if it is belong to project
            if (updateParams.AccessPointGroupId != null)
            {
                var accessPointGroup = await vhContext.AccessPointGroups.SingleAsync(x => x.ProjectId == projectId && x.AccessPointGroupId == updateParams.AccessPointGroupId);
                accessPoint.AccessPointGroupId = accessPointGroup.AccessPointGroupId;
            }

            vhContext.AccessPoints.Update(accessPoint);
            await vhContext.SaveChangesAsync();
        }

        [HttpDelete("access-points/{accessPointId:guid}")]
        public async Task Delete(Guid projectId, Guid accessPointId)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.AccessPointWrite);

            var accessPoint = await vhContext.AccessPoints
                    .SingleAsync(x =>
                    x.Server!.ProjectId == projectId && x.AccessPointId == accessPointId);

            vhContext.AccessPoints.Remove(accessPoint);
            await vhContext.SaveChangesAsync();
        }
    }
}