using GrayMint.Common.Generics;
using GrayMint.Common.Utils;
using VpnHood.AccessServer.Clients;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos.AccessTokens;
using VpnHood.AccessServer.Options;
using VpnHood.AccessServer.Persistence.Models;
using VpnHood.AccessServer.Report.Services;
using VpnHood.AccessServer.Repos;
using VpnHood.AccessServer.Utils;
using VpnHood.Manager.Common.Utils;

namespace VpnHood.AccessServer.Services;

public class AccessTokensService(
    ReportUsageService reportUsageService,
    AgentCacheClient agentCacheClient,
    VhRepo vhRepo)
{
    public async Task<AccessToken> Create(Guid projectId, AccessTokenCreateParams createParams)
    {
        var serverFarm = await vhRepo.ServerFarmGet(projectId, createParams.ServerFarmId);

        // create support id
        var supportCode = await vhRepo.AccessTokenGetMaxSupportCode(projectId) + 1;
        var accessToken = new AccessTokenModel {
            AccessTokenId = createParams.AccessTokenId ?? Guid.NewGuid(),
            ProjectId = projectId,
            ServerFarmId = serverFarm.ServerFarmId,
            ServerFarm = serverFarm,
            AccessTokenName = createParams.AccessTokenName,
            MaxTraffic = createParams.MaxTraffic,
            MaxDevice = createParams.MaxDevice,
            ExpirationTime = createParams.ExpirationTime,
            Lifetime = createParams.Lifetime,
            Tags = TagUtils.TagsToString(createParams.Tags),
            IsPublic = createParams.IsPublic,
            ClientPolicies = null,
            ClientCode = ManagerUtils.GenerateCode(AppOptions.ClientCodeDigitCount),
            ManagerCode = ManagerUtils.GenerateCode(AppOptions.ManagerCodeDigitCount),
            Secret = createParams.Secret ?? GmUtil.GenerateKey(),
            SupportCode = supportCode,
            AdRequirement = createParams.AdRequirement,
            IsEnabled = createParams.IsEnabled ?? true,
            CreatedTime = DateTime.UtcNow,
            ModifiedTime = DateTime.UtcNow,
            IsDeleted = false,
            FirstUsedTime = null,
            LastUsedTime = null,
            Description = createParams.Description
        };
        accessToken.ClientPoliciesSet(createParams.ClientPolicies.ToTokenPolicies());

        await vhRepo.AddAsync(accessToken);
        await vhRepo.SaveChangesAsync();

        return accessToken.ToDto(accessToken.ServerFarm?.ServerFarmName);
    }

    public async Task<AccessToken> Update(Guid projectId, Guid accessTokenId, AccessTokenUpdateParams updateParams)
    {
        // validate accessTokenModel.ServerFarmId
        var serverFarm = updateParams.ServerFarmId != null
            ? await vhRepo.ServerFarmGet(projectId, updateParams.ServerFarmId)
            : null;

        // update
        var accessToken = await vhRepo.AccessTokenGet(projectId, accessTokenId, includeFarm: true);
        if (updateParams.AccessTokenName != null) accessToken.AccessTokenName = updateParams.AccessTokenName;
        if (updateParams.ExpirationTime != null) accessToken.ExpirationTime = updateParams.ExpirationTime;
        if (updateParams.Lifetime != null) accessToken.Lifetime = updateParams.Lifetime;
        if (updateParams.MaxDevice != null) accessToken.MaxDevice = updateParams.MaxDevice;
        if (updateParams.MaxTraffic != null) accessToken.MaxTraffic = updateParams.MaxTraffic;
        if (updateParams.Tags != null) accessToken.Tags = TagUtils.TagsToString(updateParams.Tags.Value);
        if (updateParams.Description != null) accessToken.Description = updateParams.Description;
        if (updateParams.IsEnabled != null) accessToken.IsEnabled = updateParams.IsEnabled;
        if (updateParams.AdRequirement != null) accessToken.AdRequirement = updateParams.AdRequirement;
        if (updateParams.ClientPolicies != null) accessToken.ClientPoliciesSet(updateParams.ClientPolicies.Value.ToTokenPolicies());
        if (updateParams.ServerFarmId != null) {
            accessToken.ServerFarmId = updateParams.ServerFarmId;
            accessToken.ServerFarm = serverFarm;
        }

        if (vhRepo.HasChanges())
            accessToken.ModifiedTime = DateTime.UtcNow;

        // save and update caches
        await vhRepo.SaveChangesAsync();
        await agentCacheClient.InvalidateAccessToken(accessTokenId);

        return accessToken.ToDto(accessToken.ServerFarm?.ServerFarmName);
    }


    public async Task<string> GetAccessKey(Guid projectId, Guid accessTokenId)
    {
        var accessToken = await vhRepo.AccessTokenGet(projectId, accessTokenId, includeFarm: true);
        ArgumentNullException.ThrowIfNull(accessToken.ServerFarm);

        // create token
        var token = accessToken.ToToken(accessToken.ServerFarm.GetRequiredServerToken());
        return token.ToAccessKey();
    }

    public async Task<AccessTokenData> Get(Guid projectId, Guid accessTokenId, DateTime? usageBeginTime = null,
        DateTime? usageEndTime = null)
    {
        var items = await List(projectId, accessTokenId: accessTokenId,
            usageBeginTime: usageBeginTime, usageEndTime: usageEndTime);
        return items.Items.Single();
    }

    public async Task<ListResult<AccessTokenData>> List(Guid projectId, string? search = null,
        Guid? accessTokenId = null, Guid? serverFarmId = null,
        DateTime? usageBeginTime = null, DateTime? usageEndTime = null,
        int recordIndex = 0, int recordCount = 51)
    {
        var accessTokenViews = await vhRepo.AccessTokenList(projectId,
            accessTokenId: accessTokenId, serverFarmId: serverFarmId,
            search: search, recordIndex: recordIndex, recordCount: recordCount);

        var results = accessTokenViews
            .Items
            .Select(x => new AccessTokenData(x.AccessToken.ToDto(x.ServerFarmName)) {
                Access = x.Access?.ToDto()
            })
            .ToArray();

        // fill usage if requested
        if (usageBeginTime != null) {
            var accessTokenIds = results.Select(x => x.AccessToken.AccessTokenId).ToArray();
            var usages = await reportUsageService.GetAccessTokensUsage(projectId, accessTokenIds, serverFarmId,
                usageBeginTime, usageEndTime);

            foreach (var result in results)
                if (usages.TryGetValue(result.AccessToken.AccessTokenId, out var usage))
                    result.Usage = usage;
        }

        var listResult = new ListResult<AccessTokenData> {
            Items = results,
            TotalCount = accessTokenViews.TotalCount
        };

        return listResult;
    }

    public async Task Delete(Guid projectId, Guid[] accessTokenIds)
    {
        var deletedAccessTokenIds = await vhRepo.AccessTokenDelete(projectId, accessTokenIds);
        await vhRepo.SaveChangesAsync();

        foreach (var accessTokenId in deletedAccessTokenIds)
            await agentCacheClient.InvalidateAccessToken(accessTokenId);
    }
}