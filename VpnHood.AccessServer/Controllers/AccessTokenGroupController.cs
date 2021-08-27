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
    [Route("/api/projects/{projectId}/access-token-groups")]
    [Authorize(AuthenticationSchemes = "auth", Roles = "Admin")]
    public class AccessTokenGroupController : SuperController<AccessTokenGroupController>
    {
        public AccessTokenGroupController(ILogger<AccessTokenGroupController> logger) : base(logger)
        {
        }

        [HttpPost]
        public async Task<AccessTokenGroup> Create(Guid projectId,
            string accessTokenGroupName,
            bool makeDefault = false)
        {
            await using VhContext vhContext = new();

            // remove previous default 
            var prevDefault = vhContext.AccessTokenGroups.FirstOrDefault(x => x.ProjectId == projectId && x.IsDefault);
            if (prevDefault != null && makeDefault)
            {
                prevDefault.IsDefault = false;
                vhContext.AccessTokenGroups.Update(prevDefault);
            }

            var ret = new AccessTokenGroup
            {
                ProjectId = projectId,
                AccessTokenGroupId = Guid.NewGuid(),
                AccessTokenGroupName = accessTokenGroupName.Trim(),
                IsDefault = makeDefault || prevDefault == null
            };

            await vhContext.AccessTokenGroups.AddAsync(ret);
            await vhContext.SaveChangesAsync();
            return ret;
        }

        [HttpPut("{accessTokenGroupId}")]
        public async Task Update(Guid projectId, Guid accessTokenGroupId, string? accessTokenGroupName = null, bool makeDefault = false)
        {
            await using VhContext vhContext = new();
            var accessTokenGroup = await vhContext.AccessTokenGroups.SingleAsync(x => x.ProjectId == projectId && x.AccessTokenGroupId == accessTokenGroupId);
            if (!string.IsNullOrEmpty(accessTokenGroupName))
                accessTokenGroup.AccessTokenGroupName = accessTokenGroupName;

            // transaction required for changing default. EF can not do this due the index
            using var trans = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

            // change default
            if (!accessTokenGroup.IsDefault && makeDefault)
            {
                var prevDefault = vhContext.AccessTokenGroups.FirstOrDefault(x => x.ProjectId == projectId && x.IsDefault);
                if (prevDefault != null)
                {
                    prevDefault.IsDefault = false;
                    vhContext.AccessTokenGroups.Update(prevDefault);
                    await vhContext.SaveChangesAsync();
                }

                accessTokenGroup.IsDefault = true;
            }

            vhContext.AccessTokenGroups.Update(accessTokenGroup);
            await vhContext.SaveChangesAsync();
            trans.Complete();
        }



        [HttpGet("{accessTokenGroupId}")]
        public async Task<AccessTokenGroupData> Get(Guid projectId, Guid accessTokenGroupId)
        {
            await using VhContext vhContext = new();
            var query = from atg in vhContext.AccessTokenGroups
                             join se in vhContext.ServerEndPoints on new { key1 = atg.AccessTokenGroupId, key2 = true } equals new { key1 = se.AccessTokenGroupId, key2 = se.IsDefault } into grouping
                             from e in grouping.DefaultIfEmpty()
                             where atg.ProjectId == projectId && atg.AccessTokenGroupId == accessTokenGroupId
                             select new AccessTokenGroupData { AccessTokenGroup = atg, DefaultServerEndPoint = e };
            return await query.SingleAsync();
        }

        [HttpGet]
        public async Task<AccessTokenGroupData[]> List(Guid projectId)
        {
            await using VhContext vhContext = new();
            var res = await (from eg in vhContext.AccessTokenGroups
                             join e in vhContext.ServerEndPoints on new { key1 = eg.AccessTokenGroupId, key2 = true } equals new { key1 = e.AccessTokenGroupId, key2 = e.IsDefault } into grouping
                             from e in grouping.DefaultIfEmpty()
                             where eg.ProjectId == projectId
                             select new AccessTokenGroupData
                             {
                                 AccessTokenGroup = eg,
                                 DefaultServerEndPoint = e
                             }).ToArrayAsync();

            return res;
        }


        [HttpDelete("{accessTokenGroupId:guid}")]
        public async Task Delete(Guid projectId, Guid accessTokenGroupId)
        {
            await using VhContext vhContext = new();
            var accessTokenGroup = await vhContext.AccessTokenGroups.SingleAsync(e => e.ProjectId == projectId && e.AccessTokenGroupId == accessTokenGroupId);
            if (accessTokenGroup.IsDefault)
                throw new InvalidOperationException("A default group can not be deleted!");
            vhContext.AccessTokenGroups.Remove(accessTokenGroup);
            await vhContext.SaveChangesAsync();
        }
    }
}
