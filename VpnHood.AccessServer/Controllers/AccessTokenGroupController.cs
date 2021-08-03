using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Transactions;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Controllers
{
    [ApiController]
    [Route("{projectId}/[controller]s")]
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
            using VhContext vhContext = new();

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

        [HttpPut]
        [Route("{accessTokenGroupId}")]
        public async Task Update(Guid projectId, Guid accessTokenGroupId, string accessTokenGroupName = null, bool makeDefault = false)
        {
            using VhContext vhContext = new();
            var accessTokenGroup = await vhContext.AccessTokenGroups.SingleAsync(x => x.ProjectId == projectId && x.AccessTokenGroupId == accessTokenGroupId);
            if (!string.IsNullOrEmpty(accessTokenGroupName))
                accessTokenGroup.AccessTokenGroupName = accessTokenGroupName;

            // transaction required for changing default. EF can not do this due the index
            using var trans = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

            // change default
            if (!accessTokenGroup.IsDefault && makeDefault)
            {
                var prevDefault = vhContext.AccessTokenGroups.FirstOrDefault(x => x.ProjectId == projectId && x.IsDefault);
                prevDefault.IsDefault = false;
                vhContext.AccessTokenGroups.Update(prevDefault);
                await vhContext.SaveChangesAsync();

                accessTokenGroup.IsDefault = true;
            }

            vhContext.AccessTokenGroups.Update(accessTokenGroup);
            await vhContext.SaveChangesAsync();
            trans.Complete();
        }

        public class AccessTokenGroupData
        {
            public AccessTokenGroup AccessTokenGroup { get; set; }
            public string DefaultEndPoint { get; set; }
        }

        [HttpGet]
        [Route("{accessTokenGroupId}")]
        public async Task<AccessTokenGroupData> Get(Guid projectId, Guid accessTokenGroupId)
        {
            using VhContext vhContext = new();
            var res = await (from EG in vhContext.AccessTokenGroups
                             join E in vhContext.ServerEndPoints on new { key1 = EG.AccessTokenGroupId, key2 = true } equals new { key1 = E.AccessTokenGroupId, key2 = E.IsDefault } into grouping
                             from E in grouping.DefaultIfEmpty()
                             where EG.ProjectId == projectId && EG.AccessTokenGroupId == accessTokenGroupId
                             select new { EG, DefaultEndPoint = E.PulicEndPoint }).ToListAsync();

            return new AccessTokenGroupData
            {
                AccessTokenGroup = res.Single().EG,
                DefaultEndPoint = res.Single().DefaultEndPoint
            };
        }

        [HttpGet]
        public async Task<AccessTokenGroupData[]> List(Guid projectId)
        {
            using VhContext vhContext = new();
            var res = await (from EG in vhContext.AccessTokenGroups
                             join E in vhContext.ServerEndPoints on new { key1 = EG.AccessTokenGroupId, key2 = true } equals new { key1 = E.AccessTokenGroupId, key2 = E.IsDefault } into grouping
                             from E in grouping.DefaultIfEmpty()
                             where EG.ProjectId == projectId
                             select new AccessTokenGroupData
                             {
                                 AccessTokenGroup = EG,
                                 DefaultEndPoint = E.PulicEndPoint
                             }).ToArrayAsync();

            return res;
        }


        [HttpDelete]
        [Route("{accessTokenGroupId}")]
        public async Task Delete(Guid projectId, Guid accessTokenGroupId)
        {
            using VhContext vhContext = new();
            var accessTokenGroup = await vhContext.AccessTokenGroups.SingleAsync(e => e.ProjectId == projectId && e.AccessTokenGroupId == accessTokenGroupId);
            vhContext.AccessTokenGroups.Remove(accessTokenGroup);
        }
    }
}
