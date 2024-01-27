using GrayMint.Common.Generics;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Report.Services;
using VpnHood.Common;
using VpnHood.Common.TokenLegacy;
using VpnHood.Common.Utils;

namespace VpnHood.AccessServer.Services;

public class AccessTokensService(UsageReportService usageReportService, VhRepo vhRepo)
{
    public async Task<AccessToken> Create(Guid projectId, AccessTokenCreateParams createParams)
    {
        var serverFarm = await vhRepo.GetServerFarm(projectId, createParams.ServerFarmId);

        // create support id
        var supportCode = await vhRepo.GetMaxAccessTokenSupportCode(projectId) + 1;
        var accessToken = new AccessTokenModel
        {
            AccessTokenId = createParams.AccessTokenId ?? Guid.NewGuid(),
            ProjectId = projectId,
            ServerFarmId = serverFarm.ServerFarmId,
            AccessTokenName = createParams.AccessTokenName,
            MaxTraffic = createParams.MaxTraffic,
            MaxDevice = createParams.MaxDevice,
            ExpirationTime = createParams.ExpirationTime,
            Lifetime = createParams.Lifetime,
            Url = createParams.Url,
            IsPublic = createParams.IsPublic,
            Secret = createParams.Secret ?? VhUtil.GenerateKey(),
            SupportCode = supportCode,
            CreatedTime = DateTime.UtcNow,
            ModifiedTime = DateTime.UtcNow,
            IsEnabled = true
        };

        await vhRepo.AddAsync(accessToken);
        await vhRepo.SaveChangesAsync();

        return accessToken.ToDto(accessToken.ServerFarm?.ServerFarmName);
    }

    public async Task<AccessToken> Update(Guid projectId, Guid accessTokenId, AccessTokenUpdateParams updateParams)
    {
        // validate accessTokenModel.ServerFarmId
        var serverFarm = updateParams.ServerFarmId != null
            ? await vhRepo.GetServerFarm(projectId, updateParams.ServerFarmId) : null;

        // update
        var accessToken = await vhRepo.GetAccessToken(projectId, accessTokenId, includeServerFarm: true);
        if (updateParams.AccessTokenName != null) accessToken.AccessTokenName = updateParams.AccessTokenName;
        if (updateParams.ExpirationTime != null) accessToken.ExpirationTime = updateParams.ExpirationTime;
        if (updateParams.Lifetime != null) accessToken.Lifetime = updateParams.Lifetime;
        if (updateParams.MaxDevice != null) accessToken.MaxDevice = updateParams.MaxDevice;
        if (updateParams.MaxTraffic != null) accessToken.MaxTraffic = updateParams.MaxTraffic;
        if (updateParams.Url != null) accessToken.Url = updateParams.Url;
        if (updateParams.IsEnabled != null) accessToken.IsEnabled = updateParams.IsEnabled;
        if (updateParams.ServerFarmId != null)
        {
            accessToken.ServerFarmId = updateParams.ServerFarmId;
            accessToken.ServerFarm = serverFarm;
        }

        if (vhRepo.HasChanges())
            accessToken.ModifiedTime = DateTime.UtcNow;

        await vhRepo.SaveChangesAsync();
        return accessToken.ToDto(accessToken.ServerFarm?.ServerFarmName);
    }


    public async Task<string> GetAccessKey(Guid projectId, Guid accessTokenId)
    {
        var accessToken = await vhRepo.GetAccessToken(projectId, accessTokenId, includeServerFarm: true);

        if (string.IsNullOrEmpty(accessToken.ServerFarm!.TokenJson))
            throw new InvalidOperationException("The Farm has not been configured or it does not have at least a server with a PublicInToken access points.");

        // create token
        var token = new Token
        {
            ServerToken = VhUtil.JsonDeserialize<ServerToken>(accessToken.ServerFarm.TokenJson),
            Secret = accessToken.Secret,
            TokenId = accessToken.AccessTokenId.ToString(),
            Name = accessToken.AccessTokenName,
            SupportId = accessToken.SupportCode.ToString()
        };

#pragma warning disable CS0618 // Type or member is obsolete
        return TokenV3.FromToken(token).ToAccessKey();
#pragma warning restore CS0618 // Type or member is obsolete
    }

    public async Task<AccessTokenData> Get(Guid projectId, Guid accessTokenId, DateTime? usageBeginTime = null, DateTime? usageEndTime = null)
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

        var accessTokenViews = await vhRepo.ListAccessTokenViews(projectId,
            accessTokenId: accessTokenId, serverFarmId: serverFarmId,
            search: search, recordIndex: recordIndex, recordCount: recordCount);

        var results = accessTokenViews
            .Items
            .Select(x => new AccessTokenData(x.AccessToken.ToDto(x.ServerFarmName))
            {
                Access = x.Access?.ToDto()
            })
            .ToArray();

        // fill usage if requested
        if (usageBeginTime != null)
        {
            var accessTokenIds = results.Select(x => x.AccessToken.AccessTokenId).ToArray();
            var usages = await usageReportService.GetAccessTokensUsage(projectId, accessTokenIds, serverFarmId, usageBeginTime, usageEndTime);

            foreach (var result in results)
                if (usages.TryGetValue(result.AccessToken.AccessTokenId, out var usage))
                    result.Usage = usage;
        }

        var listResult = new ListResult<AccessTokenData>
        {
            Items = results,
            TotalCount = accessTokenViews.TotalCount
        };

        return listResult;
    }

    public async Task Delete(Guid projectId, Guid[] accessTokenIds)
    {
        await vhRepo.DeleteAccessToken(projectId, accessTokenIds);
        await vhRepo.SaveChangesAsync();
    }
}