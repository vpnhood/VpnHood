using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.Caching;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Security;
using Session = VpnHood.AccessServer.Models.Session;

namespace VpnHood.AccessServer.Controllers;

[Route("/api/projects/{projectId:guid}/sessions")]
public class SessionController : SuperController<SessionController>
{
    private readonly SystemCache _systemCache;

    public SessionController(
        ILogger<SessionController> logger,
        VhContext vhContext,
        SystemCache systemCache)
        : base(logger, vhContext)
    {
        _systemCache = systemCache;
    }

    [HttpGet]
    public async Task<Session> Get(Guid projectId, long sessionId)
    {
        await VerifyUserPermission(VhContext, projectId, Permissions.ProjectRead);
        return await _systemCache.GetSession(VhContext, sessionId);
    }

}