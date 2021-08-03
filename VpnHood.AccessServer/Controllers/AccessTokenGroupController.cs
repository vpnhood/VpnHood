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
    [Route("[controller]")]
    [Authorize(AuthenticationSchemes = "auth", Roles = "Admin")]
    public class AccessTokenGroupController : SuperController<AccessTokenGroupController>
    {
        public AccessTokenGroupController(ILogger<AccessTokenGroupController> logger) : base(logger)
        {
        }

        [HttpPost]
        [Route(nameof(Create))]
        public async Task<AccessTokenGroup> Create(Guid accountId, string accessTokenGroupName, bool makeDefault = false)
        {
            using VhContext vhContext = new();

            // remove previous default 
            var prevDefault = vhContext.AccessTokenGroups.FirstOrDefault(x => x.AccountId == accountId && x.IsDefault);
            if (prevDefault != null && makeDefault)
            {
                prevDefault.IsDefault = false;
                vhContext.AccessTokenGroups.Update(prevDefault);
            }

            var ret = new AccessTokenGroup
            {
                AccountId = accountId,
                AccessTokenGroupId = Guid.NewGuid(),
                AccessTokenGroupName = accessTokenGroupName.Trim(),
                IsDefault = makeDefault || prevDefault == null
            };

            await vhContext.AccessTokenGroups.AddAsync(ret);
            await vhContext.SaveChangesAsync();
            return ret;
        }

        public class AccessTokenGroupData
        {
            public AccessTokenGroup AccessTokenGroup { get; set; }
            public string DefaultEndPoint { get; set; }
        }

        [HttpGet]
        [Route(nameof(Get))]
        public async Task<AccessTokenGroupData> Get(Guid accountId, Guid accessTokenGroupId)
        {
            using VhContext vhContext = new();
            var res = await (from EG in vhContext.AccessTokenGroups
                             join E in vhContext.ServerEndPoints on new { key1 = EG.AccessTokenGroupId, key2 = true } equals new { key1 = E.AccessTokenGroupId, key2 = E.IsDefault } into grouping
                             from E in grouping.DefaultIfEmpty()
                             where EG.AccountId == accountId && EG.AccessTokenGroupId == accessTokenGroupId
                             select new { EG, DefaultEndPoint = E.PulicEndPoint }).ToListAsync();

            return new AccessTokenGroupData
            {
                AccessTokenGroup = res.Single().EG,
                DefaultEndPoint = res.Single().DefaultEndPoint
            };
        }

        [HttpGet]
        [Route(nameof(List))]
        public async Task<AccessTokenGroupData[]> List(Guid accountId)
        {
            using VhContext vhContext = new();
            var res = await (from EG in vhContext.AccessTokenGroups
                             join E in vhContext.ServerEndPoints on new { key1 = EG.AccessTokenGroupId, key2 = true } equals new { key1 = E.AccessTokenGroupId, key2 = E.IsDefault } into grouping
                             from E in grouping.DefaultIfEmpty()
                             where EG.AccountId == accountId
                             select new AccessTokenGroupData
                             {
                                 AccessTokenGroup = EG,
                                 DefaultEndPoint = E.PulicEndPoint
                             }).ToArrayAsync();

            return res;
        }


        [HttpDelete]
        [Route(nameof(Delete))]
        public async Task Delete(Guid accountId, Guid accessTokenGroupId)
        {
            using VhContext vhContext = new();
            var accessTokenGroup = await vhContext.AccessTokenGroups.SingleAsync(e => e.AccountId == accountId && e.AccessTokenGroupId == accessTokenGroupId);
            vhContext.AccessTokenGroups.Remove(accessTokenGroup);
        }

        [HttpPut]
        [Route(nameof(Update))]
        public async Task Update(Guid accountId, Guid accessTokenGroupId, string accessTokenGroupName = null, bool makeDefault = false)
        {
            using VhContext vhContext = new();
            var accessTokenGroup = await vhContext.AccessTokenGroups.SingleAsync(x => x.AccountId == accountId && x.AccessTokenGroupId == accessTokenGroupId);
            if (!string.IsNullOrEmpty(accessTokenGroupName))
                accessTokenGroup.AccessTokenGroupName = accessTokenGroupName;

            // transaction required for changing default. EF can not do this due the index
            using var trans = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

            // change default
            if (!accessTokenGroup.IsDefault && makeDefault)
            {
                var prevDefault = vhContext.AccessTokenGroups.FirstOrDefault(x => x.AccountId == accountId && x.IsDefault);
                prevDefault.IsDefault = false;
                vhContext.AccessTokenGroups.Update(prevDefault);
                await vhContext.SaveChangesAsync();

                accessTokenGroup.IsDefault = true;
            }

            vhContext.AccessTokenGroups.Update(accessTokenGroup);
            await vhContext.SaveChangesAsync();
            trans.Complete();
        }
    }
}
