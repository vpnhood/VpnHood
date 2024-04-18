﻿using Microsoft.AspNetCore.Authorization;
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
    [HttpGet("{adSecret}")]
    public async Task RewardAd(Guid projectId, string adSecret, 
        [FromQuery(Name = "custom_data")] string? customData, [FromQuery(Name = "reward_item")] string? rewardItem)
    {
        var project = await cacheService.GetProject(projectId);
        if (project.AdSecret != adSecret)
            throw new UnauthorizedAccessException($"The {nameof(project.AdSecret)} does not match to project.");

        _ = rewardItem; // not used yet
        if (!string.IsNullOrEmpty(customData) && customData.Length < 150)
            cacheService.RewardAd(projectId, customData);
    }
}