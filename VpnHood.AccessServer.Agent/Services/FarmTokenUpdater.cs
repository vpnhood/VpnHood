using Microsoft.Extensions.DependencyInjection;
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
        foreach (var farmId in farmIds) {
            try {
                await Update(farmId, false);
            }
            catch (Exception ex) {
                logger.LogWarning(ex, "Could not update farm token. FarmId: {FarmId}", farmId);
            }
        }

        // reset IsPendingUpload
        await vhAgentRepo.FarmTokenRepoSetPendingUpload(farmIds: farmIds);
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

        if (FarmTokenBuilder.UpdateIfChanged(serverFarm) &&
            cacheRepo.ServerFarms.TryGetValue(farmId, out var farmCache))
            farmCache.TokenJson = serverFarm.TokenJson;

        if (saveChanged)
            await vhAgentRepo.SaveChangesAsync();
    }
}