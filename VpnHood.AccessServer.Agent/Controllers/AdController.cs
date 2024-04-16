using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VpnHood.AccessServer.Agent.Services;

namespace VpnHood.AccessServer.Agent.Controllers;

[ApiController]
[Route("/api/ad")]
[Authorize(AgentPolicy.SystemPolicy)]
public class AdController(
    CacheService cacheService,
    SessionService sessionService) 
    : ControllerBase
{
    [HttpPost("{projectId:guid}/{adSecret}")]
    public Task GoogleAdReceiver(Guid projectId, string adSecret)
    {
        //todo
        cacheService.AddAd(projectId, "adData");
        return Task.CompletedTask;
    }
}