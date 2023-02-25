using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using VpnHood.AccessServer.MultiLevelAuthorization.Services;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Security;
using VpnHood.AccessServer.Services;

namespace VpnHood.AccessServer.Controllers;

[Route("/api/v{version:apiVersion}/system")]
public class SystemController : SuperController<SystemController>
{
    private readonly SyncService _syncService;

    public SystemController(
        ILogger<SystemController> logger,
        VhContext vhContext,
        MultilevelAuthService multilevelAuthService, 
        SyncService syncService)
        : base(logger, vhContext, multilevelAuthService)
    {
        _syncService = syncService;
    }

    [HttpPost]
    public async Task Sync()
    {
        var curUserId = await GetCurrentUserId(VhContext);
        await VerifyUserPermission(curUserId, Permissions.ProjectCreate);

        await _syncService.Sync();
    }
}