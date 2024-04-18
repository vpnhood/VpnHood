using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VpnHood.AccessServer.Agent.Services;

namespace VpnHood.AccessServer.Agent.Controllers;

[ApiController]
[Route("/api/ad/projects/{projectId:guid}/")]
[Authorize(AgentPolicy.SystemPolicy)]
public class AdController(
    CacheService cacheService) 
    : ControllerBase
{
    [HttpGet("{adRewardSecret}")]
    public async Task RewardAd(Guid projectId, string adRewardSecret, 
        [FromQuery(Name = "custom_data")] string? customData, [FromQuery(Name = "reward_item")] string? rewardItem)
    {
        var project = await cacheService.GetProject(projectId);
        if (project.AdRewardSecret != adRewardSecret)
            throw new UnauthorizedAccessException($"The {nameof(project.AdRewardSecret)} does not match to project.");

        _ = rewardItem; // not used yet
        if (!string.IsNullOrEmpty(customData) && customData.Length < 150)
            cacheService.RewardAd(projectId, customData);
    }
}