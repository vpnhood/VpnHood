using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Security;
using System.Linq;
using VpnHood.AccessServer.DTOs;

namespace VpnHood.AccessServer.Controllers
{
    [Route("/api/projects/{projectId:guid}/ip-locks")]
    public class IpLockController : SuperController<IpLockController>
    {
        public IpLockController(ILogger<IpLockController> logger) : base(logger)
        {
        }

        [HttpPost]
        public async Task<IpLock> Create(Guid projectId, IpLockCreateParams createParams)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.IpLockWrite);

            var ipLock = new IpLock
            {
                IpAddress = createParams.IpAddress.ToString(),
                Description = createParams.Description,
                LockedTime = createParams.IsLocked ? DateTime.UtcNow : null,
                ProjectId = projectId
            };
            var res = await vhContext.IpLocks.AddAsync(ipLock);
            await vhContext.SaveChangesAsync();
            return res.Entity;
        }

        [HttpPatch("{ip}")]
        public async Task<IpLock> Update(Guid projectId, string ip, IpLockUpdateParams updateParams)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.IpLockWrite);

            var ipLock = await vhContext.IpLocks.SingleAsync(x => x.ProjectId == projectId && x.IpAddress == ip);
            if (updateParams.IsLocked != null) ipLock.LockedTime = updateParams.IsLocked && ipLock.LockedTime == null ? DateTime.UtcNow : null;
            if (updateParams.Description != null) ipLock.Description = updateParams.Description;

            var res = vhContext.IpLocks.Update(ipLock);
            await vhContext.SaveChangesAsync();
            return res.Entity;
        }

        [HttpDelete("{ip}")]
        public async Task Delete(Guid projectId, string ip)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.IpLockWrite);

            var ipLock = await vhContext.IpLocks.SingleAsync(x => x.ProjectId == projectId && x.IpAddress == ip);
            vhContext.IpLocks.Remove(ipLock);
            await vhContext.SaveChangesAsync();
        }

        [HttpGet("{ip}")]
        public async Task<IpLock> Get(Guid projectId, string ip)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.ProjectRead);

            return await vhContext.IpLocks.SingleAsync(x => x.ProjectId == projectId && x.IpAddress == ip.ToLower());
        }

        [HttpGet]
        public async Task<IpLock[]> List(Guid projectId, int recordIndex = 0, int recordCount = 300)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.ProjectRead);

            var query = vhContext.IpLocks
                .Where(x => x.ProjectId == projectId)
                .Skip(recordIndex)
                .Take(recordCount);

            return await query.ToArrayAsync();
        }
    }
}