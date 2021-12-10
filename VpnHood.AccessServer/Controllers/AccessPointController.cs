using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.DTOs;
using VpnHood.AccessServer.Exceptions;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Security;

namespace VpnHood.AccessServer.Controllers
{
    [Route("/api/projects/{projectId:guid}/access-points")]
    public class AccessPointController : SuperController<AccessPointController>
    {
        public AccessPointController(ILogger<AccessPointController> logger) : base(logger)
        {
        }

        [HttpPost]
        public async Task<AccessPoint> Create(Guid projectId, AccessPointCreateParams createParams)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.AccessPointWrite);

            // check user quota
            using var singleRequest = SingleRequest.Start($"CreateAccessPoint_{CurrentUserId}");
            if (vhContext.AccessPoints.Count(x => x.ServerId == createParams.ServerId) >= QuotaConstants.AccessPointCount)
                throw new QuotaException(nameof(VhContext.AccessPoints), QuotaConstants.AccessPointCount);


            // find default AccessPointGroup
            var accessPointGroup = await vhContext.AccessPointGroups
                .SingleAsync(x => x.ProjectId == projectId && x.AccessPointGroupId == createParams.AccessPointGroupId);

            // validate serverId project ownership
            var server = await vhContext.Servers
                .SingleAsync(x => x.ProjectId == projectId && x.ServerId == createParams.ServerId);

            // update server ConfigCode
            server.ConfigCode = Guid.NewGuid();
            vhContext.Servers.Update(server);

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

        [HttpGet]
        public async Task<AccessPoint[]> List(Guid projectId, Guid? serverId = null, Guid? accessPointGroupId = null)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.ProjectRead);

            var query = vhContext.AccessPoints
                .Include(x => x.Server)
                .Include(x => x.AccessPointGroup)
                .Where(x => x.Server!.ProjectId == projectId);

            if (serverId != null)
                query = query.Where(x => x.ServerId == serverId);

            if (accessPointGroupId != null)
                query = query.Where(x => x.AccessPointGroupId == accessPointGroupId);

            var ret = await query.ToArrayAsync();
            return ret;
        }

        [HttpGet("{accessPointId:guid}")]
        public async Task<AccessPoint> Get(Guid projectId, Guid accessPointId)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.ProjectRead);

            var accessPoint = await vhContext.AccessPoints
                .Include(e => e.Server)
                .Include(e => e.AccessPointGroup)
                .SingleAsync(e => e.Server!.ProjectId == projectId && e.AccessPointGroup != null && e.AccessPointId == accessPointId);

            // check access
            return accessPoint;
        }

        [HttpPatch("{accessPointId:guid}")]
        public async Task Update(Guid projectId, Guid accessPointId, AccessPointUpdateParams updateParams)
        {
            if (updateParams.IpAddress != null) AccessUtil.ValidateIpEndPoint(updateParams.IpAddress);

            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.AccessPointWrite);

            // get previous object
            var accessPoint = await vhContext.AccessPoints
                .Include(x=>x.Server)
                .SingleAsync(x => x.Server!.ProjectId == projectId && x.AccessPointId == accessPointId);

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

            // Schedule server reconfig
            accessPoint.Server!.ConfigCode = Guid.NewGuid();
            vhContext.Servers.Update(accessPoint.Server);

            vhContext.AccessPoints.Update(accessPoint);
            await vhContext.SaveChangesAsync();
        }

        [HttpDelete("{accessPointId:guid}")]
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