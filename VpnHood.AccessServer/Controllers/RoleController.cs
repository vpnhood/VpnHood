using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.MultiLevelAuthorization.Services;
using VpnHood.AccessServer.Persistence;

namespace VpnHood.AccessServer.Controllers;

[Route("/api/projects/{projectId:guid}/roles")]
public class RoleController : SuperController<RoleController>
{
    public RoleController(ILogger<RoleController> logger, VhContext vhContext, MultilevelAuthService multilevelAuthService) 
        : base(logger, vhContext, multilevelAuthService)
    {
    }

    [HttpPost]
    public Task AddUser(Guid projectId, Guid roleId, string userEmail)
    {
        throw new NotImplementedException();
    }
}