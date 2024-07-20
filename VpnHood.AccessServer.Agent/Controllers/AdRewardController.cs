using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VpnHood.AccessServer.Agent.Services;

namespace VpnHood.AccessServer.Agent.Controllers;

[ApiController]
[Route("/api/projects/{projectId:guid}/ad-rewards")]
[Authorize(AgentPolicy.SystemPolicy)]
public class AdRewardController(
    ILogger<AdRewardController> logger,
    CacheService cacheService)
    : ControllerBase
{
    [HttpGet("{adRewardSecret}")]
    [AllowAnonymous]
    public async Task RewardAd(Guid projectId, string adRewardSecret,
        [FromQuery(Name = "custom_data")] string? customData,
        [FromQuery(Name = "reward_item")] string? rewardItem = null)
    {
        logger.LogTrace("RewardAd has been received. ProjectId: {ProjectId}, AdData: {AdData}", projectId, customData);

        var project = await cacheService.GetProject(projectId);
        if (project.AdRewardSecret != adRewardSecret)
            throw new UnauthorizedAccessException($"The {nameof(project.AdRewardSecret)} does not match to project.");

        _ = rewardItem; // not used yet
        if (!string.IsNullOrEmpty(customData) && customData.Length < 150)
            cacheService.Ad_AddRewardData(projectId, customData);
    }
}