using System.Net.Mime;
using GrayMint.Common.Generics;
using GrayMint.Common.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VpnHood.AccessServer.Dtos.AccessTokens;
using VpnHood.AccessServer.Security;
using VpnHood.AccessServer.Services;

namespace VpnHood.AccessServer.Controllers;

[ApiController]
[Authorize]
[Route("/api/v{version:apiVersion}/projects/{projectId}/access-tokens")]
public class AccessTokensController(
    AccessTokensService accessTokensService,
    SubscriptionService subscriptionService) : ControllerBase
{
    [HttpPost]
    [AuthorizeProjectPermission(Permissions.AccessTokenWrite)]
    public async Task<AccessToken> Create(Guid projectId, AccessTokenCreateParams createParams)
    {
        // check user quota
        using var singleRequest = await AsyncLock.LockAsync($"{projectId}_CreateAccessTokens");
        await subscriptionService.AuthorizeCreateAccessToken(projectId);

        return await accessTokensService.Create(projectId, createParams);
    }

    [HttpPatch("{accessTokenId:guid}")]
    [AuthorizeProjectPermission(Permissions.AccessTokenWrite)]
    public Task<AccessToken> Update(Guid projectId, Guid accessTokenId, AccessTokenUpdateParams updateParams)
    {
        return accessTokensService.Update(projectId, accessTokenId, updateParams);
    }

    [HttpGet("{accessTokenId:guid}/access-key")]
    [AuthorizeProjectPermission(Permissions.AccessTokenReadAccessKey)]
    [Produces(MediaTypeNames.Application.Json)]
    public Task<string> GetAccessKey(Guid projectId, Guid accessTokenId)
    {
        return accessTokensService.GetAccessKey(projectId, accessTokenId);
    }

    [HttpGet("{accessTokenId}")]
    [AuthorizeProjectPermission(Permissions.ProjectRead)]
    public Task<AccessTokenData> Get(Guid projectId, Guid accessTokenId, DateTime? usageBeginTime = null,
        DateTime? usageEndTime = null)
    {
        return accessTokensService.Get(projectId, accessTokenId, usageBeginTime, usageEndTime);
    }

    [HttpGet]
    [AuthorizeProjectPermission(Permissions.ProjectRead)]
    public async Task<ListResult<AccessTokenData>> List(Guid projectId, string? search = null,
        Guid? accessTokenId = null, Guid? serverFarmId = null,
        DateTime? usageBeginTime = null, DateTime? usageEndTime = null,
        int recordIndex = 0, int recordCount = 51)
    {
        await subscriptionService.VerifyUsageQueryPermission(projectId, usageBeginTime, usageEndTime);

        return await accessTokensService.List(projectId: projectId, search: search, accessTokenId: accessTokenId,
            serverFarmId: serverFarmId,
            usageBeginTime: usageBeginTime, usageEndTime: usageEndTime, recordIndex: recordIndex,
            recordCount: recordCount);
    }

    [HttpDelete]
    [AuthorizeProjectPermission(Permissions.AccessTokenWrite)]
    public Task DeleteMany(Guid projectId, Guid[] accessTokenIds)
    {
        return accessTokensService.Delete(projectId, accessTokenIds);
    }


    [HttpDelete("{accessTokenId:guid}")]
    [AuthorizeProjectPermission(Permissions.AccessTokenWrite)]
    public Task Delete(Guid projectId, Guid accessTokenId)
    {
        return accessTokensService.Delete(projectId, [accessTokenId]);
    }
}