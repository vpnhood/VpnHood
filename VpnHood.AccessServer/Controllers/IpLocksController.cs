﻿using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.MultiLevelAuthorization.Services;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Security;

namespace VpnHood.AccessServer.Controllers;

[Route("/api/v{version:apiVersion}/projects/{projectId:guid}/ip-locks")]
public class IpLocksController : SuperController<IpLocksController>
{
    public IpLocksController(ILogger<IpLocksController> logger, VhContext vhContext, MultilevelAuthService multilevelAuthService) 
        : base(logger, vhContext, multilevelAuthService)
    {
    }

    [HttpPost]
    public async Task<IpLock> Create(Guid projectId, IpLockCreateParams createParams)
    {
        await VerifyUserPermission( projectId, Permissions.IpLockWrite);

        var ipLock = new IpLockModel
        {
            IpAddress = createParams.IpAddress.ToString(),
            Description = createParams.Description,
            LockedTime = createParams.IsLocked ? DateTime.UtcNow : null,
            ProjectId = projectId
        };
        var res = await VhContext.IpLocks.AddAsync(ipLock);
        await VhContext.SaveChangesAsync();
        return res.Entity.ToDto();
    }

    [HttpPatch("{ip}")]
    public async Task<IpLock> Update(Guid projectId, string ip, IpLockUpdateParams updateParams)
    {
        await VerifyUserPermission( projectId, Permissions.IpLockWrite);

        var ipLock = await VhContext.IpLocks.SingleAsync(x => x.ProjectId == projectId && x.IpAddress == ip);
        if (updateParams.IsLocked != null) ipLock.LockedTime = updateParams.IsLocked && ipLock.LockedTime == null ? DateTime.UtcNow : null;
        if (updateParams.Description != null) ipLock.Description = updateParams.Description;

        var res = VhContext.IpLocks.Update(ipLock);
        await VhContext.SaveChangesAsync();
        return res.Entity.ToDto();
    }

    [HttpDelete("{ip}")]
    public async Task Delete(Guid projectId, string ip)
    {
        await VerifyUserPermission( projectId, Permissions.IpLockWrite);

        var ipLock = await VhContext.IpLocks.SingleAsync(x => x.ProjectId == projectId && x.IpAddress == ip);
        VhContext.IpLocks.Remove(ipLock);
        await VhContext.SaveChangesAsync();
    }

    [HttpGet("{ip}")]
    public async Task<IpLock> Get(Guid projectId, string ip)
    {
        await VerifyUserPermission( projectId, Permissions.ProjectRead);

        var ipLockModel = await VhContext.IpLocks.SingleAsync(x => x.ProjectId == projectId && x.IpAddress == ip.ToLower());
        return ipLockModel.ToDto();
    }

    [HttpGet]
    public async Task<IpLock[]> List(Guid projectId, int recordIndex = 0, int recordCount = 300)
    {
        await VerifyUserPermission( projectId, Permissions.ProjectRead);

        var ret = await VhContext.IpLocks
            .Where(x => x.ProjectId == projectId)
            .Select(x=>x.ToDto())
            .Skip(recordIndex)
            .Take(recordCount)
            .ToArrayAsync();

        return ret;
    }
}