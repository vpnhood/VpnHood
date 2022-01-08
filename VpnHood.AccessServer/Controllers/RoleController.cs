using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace VpnHood.AccessServer.Controllers;

[Route("/api/projects/{projectId:guid}/roles")]
public class RoleController : SuperController<RoleController>
{
    public RoleController(ILogger<RoleController> logger) : base(logger)
    {
    }

    [HttpPost]
    public Task AddUser(Guid projectId, Guid roleId, string userEmail)
    {
        throw new NotImplementedException();
    }
}