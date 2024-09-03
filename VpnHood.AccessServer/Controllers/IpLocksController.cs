using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos.IpLocks;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Persistence.Models;
using VpnHood.AccessServer.Security;

namespace VpnHood.AccessServer.Controllers;

[ApiController]
[Authorize]
[Route("/api/v{version:apiVersion}/projects/{projectId}/ip-locks")]
public class IpLocksController(VhContext vhContext) : ControllerBase
{
    [HttpPost]
    [AuthorizeProjectPermission(Permissions.IpLockWrite)]
    public async Task<IpLock> Create(Guid projectId, IpLockCreateParams createParams)
    {
        var ipLock = new IpLockModel {
            IpAddress = createParams.IpAddress.ToString(),
            Description = createParams.Description,
            LockedTime = createParams.IsLocked ? DateTime.UtcNow : null,
            ProjectId = projectId
        };
        var res = await vhContext.IpLocks.AddAsync(ipLock);
        await vhContext.SaveChangesAsync();
        return res.Entity.ToDto();
    }

    [HttpPatch("{ip}")]
    [AuthorizeProjectPermission(Permissions.IpLockWrite)]
    public async Task<IpLock> Update(Guid projectId, string ip, IpLockUpdateParams updateParams)
    {
        var ipLock = await vhContext.IpLocks.SingleAsync(x => x.ProjectId == projectId && x.IpAddress == ip);
        if (updateParams.IsLocked != null)
            ipLock.LockedTime = updateParams.IsLocked && ipLock.LockedTime == null ? DateTime.UtcNow : null;
        if (updateParams.Description != null) ipLock.Description = updateParams.Description;

        var res = vhContext.IpLocks.Update(ipLock);
        await vhContext.SaveChangesAsync();
        return res.Entity.ToDto();
    }

    [HttpDelete("{ip}")]
    [AuthorizeProjectPermission(Permissions.IpLockWrite)]
    public async Task Delete(Guid projectId, string ip)
    {
        var ipLock = await vhContext.IpLocks.SingleAsync(x => x.ProjectId == projectId && x.IpAddress == ip);
        vhContext.IpLocks.Remove(ipLock);
        await vhContext.SaveChangesAsync();
    }

    [HttpGet("{ip}")]
    [AuthorizeProjectPermission(Permissions.ProjectRead)]
    public async Task<IpLock> Get(Guid projectId, string ip)
    {
        var ipLockModel =
            await vhContext.IpLocks.SingleAsync(x => x.ProjectId == projectId && x.IpAddress == ip.ToLower());
        return ipLockModel.ToDto();
    }

    [HttpGet]
    [AuthorizeProjectPermission(Permissions.ProjectRead)]
    public async Task<IpLock[]> List(Guid projectId, string? search = null, int recordIndex = 0, int recordCount = 300)
    {
        var ret = await vhContext.IpLocks
            .Where(x => x.ProjectId == projectId)
            .Where(x =>
                string.IsNullOrEmpty(search) ||
                x.IpAddress.Contains(search))
            .OrderBy(x => x.LockedTime)
            .Select(x => x.ToDto())
            .Skip(recordIndex)
            .Take(recordCount)
            .ToArrayAsync();

        return ret;
    }
}