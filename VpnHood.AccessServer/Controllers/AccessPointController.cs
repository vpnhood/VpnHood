using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.Clients;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Exceptions;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.MultiLevelAuthorization.Services;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Security;

namespace VpnHood.AccessServer.Controllers;

[Route("/api/projects/{projectId:guid}/access-points")]
public class AccessPointController : SuperController<AccessPointController>
{
    private readonly AgentCacheClient _agentCacheClient;

    public AccessPointController(
        VhContext vhContext,
        ILogger<AccessPointController> logger, 
        MultilevelAuthService multilevelAuthService, 
        AgentCacheClient agentCacheClient) 
        : base(logger, vhContext, multilevelAuthService)
    {
        _agentCacheClient = agentCacheClient;
    }

    [HttpPost]
    public async Task<AccessPoint> Create(Guid projectId, AccessPointCreateParams createParams)
    {
        await VerifyUserPermission(VhContext, projectId, Permissions.AccessPointWrite);

        // check user quota
        using var singleRequest = SingleRequest.Start($"CreateAccessPoint_{CurrentUserId}");
        if (VhContext.AccessPoints.Count(x => x.ServerId == createParams.ServerId) >= QuotaConstants.AccessPointCount)
            throw new QuotaException(nameof(VhContext.AccessPoints), QuotaConstants.AccessPointCount);


        // find default AccessPointGroup
        var accessPointGroup = await VhContext.AccessPointGroups
            .SingleAsync(x => x.ProjectId == projectId && x.AccessPointGroupId == createParams.AccessPointGroupId);

        // validate serverId project ownership
        var server = await VhContext.Servers
            .SingleAsync(x => x.ProjectId == projectId && x.ServerId == createParams.ServerId);

        // Make sure ServerFarm is manual
        if (server.AccessPointGroupId != null)
            throw new InvalidOperationException("To configure access points, you must set the server's farm manual.");

        // add the access point
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
        await VhContext.AccessPoints.AddAsync(ret);

        // update server ConfigCode
        server.ConfigCode = Guid.NewGuid();
        await VhContext.SaveChangesAsync();
        await _agentCacheClient.InvalidateServer(server.ServerId);
        return ret;
    }

    [HttpGet]
    public async Task<AccessPoint[]> List(Guid projectId, Guid? serverId = null, Guid? accessPointGroupId = null)
    {
        await VerifyUserPermission(VhContext, projectId, Permissions.ProjectRead);

        var query = VhContext.AccessPoints
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
        await VerifyUserPermission(VhContext, projectId, Permissions.ProjectRead);

        var accessPoint = await VhContext.AccessPoints
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

        await VerifyUserPermission(VhContext, projectId, Permissions.AccessPointWrite);

        // get previous object
        var accessPoint = await VhContext.AccessPoints
            .Include(x=>x.Server)
            .SingleAsync(x => x.Server!.ProjectId == projectId && x.AccessPointId == accessPointId);

        // Make sure ServerFarm is manual
        if (accessPoint.Server!.AccessPointGroupId != null)
            throw new InvalidOperationException("To configure access points, you must set the server's farm manual.");

        // update
        if (updateParams.IpAddress != null) accessPoint.IpAddress = updateParams.IpAddress;
        if (updateParams.TcpPort != null) accessPoint.TcpPort = updateParams.TcpPort;
        if (updateParams.UdpPort != null) accessPoint.UdpPort = updateParams.UdpPort;
        if (updateParams.AccessPointMode != null) accessPoint.AccessPointMode = updateParams.AccessPointMode;
        if (updateParams.IsListen != null) accessPoint.IsListen = updateParams.IsListen;

        // update AccessPointGroupId if it is belong to project
        if (updateParams.AccessPointGroupId != null)
        {
            var accessPointGroup = await VhContext.AccessPointGroups.SingleAsync(x => x.ProjectId == projectId && x.AccessPointGroupId == updateParams.AccessPointGroupId);
            accessPoint.AccessPointGroupId = accessPointGroup.AccessPointGroupId;
        }

        // update the access point
        VhContext.AccessPoints.Update(accessPoint);

        // Schedule server reconfig
        accessPoint.Server!.ConfigCode = Guid.NewGuid();
        await VhContext.SaveChangesAsync();
        await _agentCacheClient.InvalidateServer(accessPoint.ServerId);
    }

    [HttpDelete("{accessPointId:guid}")]
    public async Task Delete(Guid projectId, Guid accessPointId)
    {
        await VerifyUserPermission(VhContext, projectId, Permissions.AccessPointWrite);

        var accessPoint = await VhContext.AccessPoints
            .Include(x=>x.Server)
            .SingleAsync(x => x.Server!.ProjectId == projectId && x.AccessPointId == accessPointId);

        // Make sure server farm is manual
        if (accessPoint.Server!.AccessPointGroupId != null)
            throw new InvalidOperationException("To configure access points, you must set the server's farm manual.");

        // update the access point
        VhContext.AccessPoints.Remove(accessPoint);

        // Schedule server reconfig
        accessPoint.Server!.ConfigCode = Guid.NewGuid();
        VhContext.Servers.Update(accessPoint.Server);

        await VhContext.SaveChangesAsync();
        await _agentCacheClient.InvalidateProject(projectId);
    }
}