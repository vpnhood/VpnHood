using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.DTOs;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Security;

namespace VpnHood.AccessServer.Controllers
{
    [Route("/api/projects/{projectId:guid}/clients")]
    public class ClientController : SuperController<ClientController>
    {
        public ClientController(ILogger<ClientController> logger) : base(logger)
        {
        }

        [HttpGet("{clientId:guid}")]
        public async Task<ProjectClient> Get(Guid projectId, Guid clientId)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.ClientRead);

            return await vhContext.ProjectClients.SingleAsync(x => x.ProjectId == projectId && x.ClientId == clientId);
        }

        [HttpGet]
        public async Task<UsageData[]> List(Guid projectId,
            Guid? deviceId = null, Guid? serverId = null, Guid? accessId = null,
            Guid? accessTokenId = null, Guid? accessPointGroupId = null,
            DateTime? startTime = null, DateTime? endTime = null, int recordIndex = 0, int recordCount = 300)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.ClientRead);

            var usages = UsageController.Query(groupByModel: typeof(ProjectClient), vhContext: vhContext,
                projectId: projectId, deviceId: deviceId, serverId: serverId, accessId: accessId, accessTokenId: accessTokenId, accessPointGroupId: accessPointGroupId,
                startTime: startTime, endTime: endTime);

            usages = usages
                .OrderByDescending(x => x.LastTime)
                .Skip(recordIndex)
                .Take(recordCount);

            // create output
            var query = from usage in usages
                        join accessUsage in vhContext.AccessUsages
                            .Include(x => x.Access)
                            .Include(x => x.Server)
                            .Include(x => x.Session)
                            .Include(x => x.Session!.Client)
                            .Include(x => x.Session!.AccessToken)
                            .Include(x => x.Session!.AccessToken!.AccessPointGroup)
                         on usage.LastAccessUsageId equals accessUsage.AccessUsageId
                        select new UsageData
                        {
                            LastAccessUsage = accessUsage,
                            Usage = usage,
                        };

            vhContext.DebugMode = true;
            var res = await query.ToArrayAsync();
            return res;
        }
    }
}