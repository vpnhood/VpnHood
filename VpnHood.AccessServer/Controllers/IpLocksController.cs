using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Security;

namespace VpnHood.AccessServer.Controllers;

[ApiController]
[Authorize]
[Route("/api/v{version:apiVersion}/projects/{projectId:guid}/ip-locks")]
public class IpLocksController : ControllerBase
{
    private readonly VhContext _vhContext;

    public IpLocksController(
        VhContext vhContext)
    {
        _vhContext = vhContext;
    }

    [HttpPost]
    [AuthorizeProjectPermission(Permissions.IpLockWrite)]
    public async Task<IpLock> Create(Guid projectId, IpLockCreateParams createParams)
    {
        var ipLock = new IpLockModel
        {
            IpAddress = createParams.IpAddress.ToString(),
            Description = createParams.Description,
            LockedTime = createParams.IsLocked ? DateTime.UtcNow : null,
            ProjectId = projectId
        };
        var res = await _vhContext.IpLocks.AddAsync(ipLock);
        await _vhContext.SaveChangesAsync();
        return res.Entity.ToDto();
    }

    [HttpPatch("{ip}")]
    [AuthorizeProjectPermission(Permissions.IpLockWrite)]
    public async Task<IpLock> Update(Guid projectId, string ip, IpLockUpdateParams updateParams)
    {
        var ipLock = await _vhContext.IpLocks.SingleAsync(x => x.ProjectId == projectId && x.IpAddress == ip);
        if (updateParams.IsLocked != null) ipLock.LockedTime = updateParams.IsLocked && ipLock.LockedTime == null ? DateTime.UtcNow : null;
        if (updateParams.Description != null) ipLock.Description = updateParams.Description;

        var res = _vhContext.IpLocks.Update(ipLock);
        await _vhContext.SaveChangesAsync();
        return res.Entity.ToDto();
    }

    [HttpDelete("{ip}")]
    [AuthorizeProjectPermission(Permissions.IpLockWrite)]
    public async Task Delete(Guid projectId, string ip)
    {
        var ipLock = await _vhContext.IpLocks.SingleAsync(x => x.ProjectId == projectId && x.IpAddress == ip);
        _vhContext.IpLocks.Remove(ipLock);
        await _vhContext.SaveChangesAsync();
    }

    [HttpGet("{ip}")]
    [AuthorizeProjectPermission(Permissions.ProjectRead)]
    public async Task<IpLock> Get(Guid projectId, string ip)
    {
        var ipLockModel = await _vhContext.IpLocks.SingleAsync(x => x.ProjectId == projectId && x.IpAddress == ip.ToLower());
        return ipLockModel.ToDto();
    }

    [HttpGet]
    [AuthorizeProjectPermission(Permissions.ProjectRead)]
    public async Task<IpLock[]> List(Guid projectId, string? search = null, int recordIndex = 0, int recordCount = 300)
    {
        var ret = await _vhContext.IpLocks
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