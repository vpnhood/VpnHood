using VpnHood.AccessServer.Agent.Repos;
using VpnHood.AccessServer.Agent.Utils;

namespace VpnHood.AccessServer.Agent.Services;

public class FarmTokenUpdater(
    CacheRepo cacheRepo,
    VhAgentRepo vhAgentRepo,
    IServiceScopeFactory serviceScopeFactory,
    ILogger<FarmTokenUpdater> logger)

{
    public async Task UpdateAndSaveChanges(Guid[] farmIds)
    {
        foreach (var farmId in farmIds.Distinct()) {
            try {
                await Update(farmId, false);
            }
            catch (Exception ex) {
                logger.LogWarning(ex, "Could not update farm token. FarmId: {FarmId}", farmId);
            }
        }

        // save changed farms
        await vhAgentRepo.SaveChangesAsync();

        // upload pending tokens
        _ = FarmTokenRepoUploader.UploadPendingTokensJob(serviceScopeFactory, CancellationToken.None);
    }

    private async Task Update(Guid farmId, bool saveChanged)
    {
        var serverFarm = await vhAgentRepo.ServerFarmGet(farmId,
            includeTokenRepos: true,
            includeServersAndAccessPoints: true,
            includeCertificates: true);

        // update token
        FarmTokenBuilder.UpdateIfChanged(serverFarm);
        
        // update token in cache if changed and exists in cache
        if (cacheRepo.ServerFarms.TryGetValue(farmId, out var farmCache))
            farmCache.TokenJson = serverFarm.TokenJson;

        if (saveChanged)
            await vhAgentRepo.SaveChangesAsync();
    }
}