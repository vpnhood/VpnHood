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
    [Route("/api/projects/{projectId:guid}/ip-lock")]
    public class IpBlockController : SuperController<IpBlockController>
    {
        public IpBlockController(ILogger<IpBlockController> logger) : base(logger)
        {
        }

        [HttpPost("{ip}")]
        public async Task<IpBlock> Create(Guid projectId, IpBlockCreateParams createParams)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.IpLockWrite);

            var ipLock = new IpBlock
            {
                Ip = createParams.IpAddress.ToString(),
                Description = createParams.Description,
                BlockedTime = createParams.IsBlocked ? DateTime.UtcNow : null,
                ProjectId = projectId
            };
            var res = await vhContext.IpBlocks.AddAsync(ipLock);
            await vhContext.SaveChangesAsync();
            return res.Entity;
        }

        [HttpPost("{ip}")]
        public async Task<IpBlock> Update(Guid projectId, string ip, IpBlockUpdateParams updateParams)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.IpLockWrite);

            var ipLock = await vhContext.IpBlocks.SingleAsync(x => x.ProjectId == projectId && x.Ip == ip);
            if (updateParams.IsLocked != null) ipLock.BlockedTime = updateParams.IsLocked && ipLock.BlockedTime == null ? DateTime.UtcNow : null;
            if (updateParams.Description != null) ipLock.Description = updateParams.Description;

            var res = vhContext.IpBlocks.Update(ipLock);
            await vhContext.SaveChangesAsync();
            return res.Entity;
        }

        [HttpDelete]
        public async Task Delete(Guid projectId, string ip)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.IpLockWrite);

            var ipLock = await vhContext.IpBlocks.SingleAsync(x => x.ProjectId == projectId && x.Ip == ip);
            vhContext.IpBlocks.Remove(ipLock);
            await vhContext.SaveChangesAsync();
        }

        [HttpGet("{ip}")]
        public async Task<IpBlock> Get(Guid projectId, string ip)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.ProjectRead);

            return await vhContext.IpBlocks.SingleAsync(x => x.ProjectId == projectId && x.Ip == ip.ToLower());
        }

        [HttpGet]
        public async Task<IpBlock[]> List(Guid projectId, int recordIndex = 0, int recordCount = 300)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.ProjectRead);

            var query = vhContext.IpBlocks
                .Where(x => x.ProjectId == projectId)
                .Skip(recordIndex)
                .Take(recordCount);

            return await query.ToArrayAsync();
        }
    }
}