using GrayMint.Common.AspNetCore.SimpleRoleAuthorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using VpnHood.AccessServer.Security;
using VpnHood.AccessServer.Services;

namespace VpnHood.AccessServer.Controllers;

[ApiController]
[Authorize]
[Route("/api/v{version:apiVersion}/system")]
public class SystemController : ControllerBase
{
    private readonly SyncService _syncService;

    public SystemController(
        SyncService syncService)
        
    {
        _syncService = syncService;
    }

    [HttpPost]
    [AuthorizePermission(Permissions.Sync)]
    public async Task Sync()
    {
        await _syncService.Sync();
    }
}