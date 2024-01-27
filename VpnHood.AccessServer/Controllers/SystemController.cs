using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VpnHood.AccessServer.Security;
using VpnHood.AccessServer.Services;

namespace VpnHood.AccessServer.Controllers;

[ApiController]
[Authorize]
[Route("/api/v{version:apiVersion}/system")]
public class SystemController(SyncService syncService) : ControllerBase
{
    [HttpPost]
    [AuthorizeProjectPermission(Permissions.Sync)]
    public Task Sync()
    {
        return syncService.Sync();
    }
}