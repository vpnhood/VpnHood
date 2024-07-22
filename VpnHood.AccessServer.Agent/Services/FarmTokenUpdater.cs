﻿using VpnHood.AccessServer.Agent.Repos;
using VpnHood.AccessServer.Persistence.Utils;

namespace VpnHood.AccessServer.Agent.Services;

public class FarmTokenUpdater(
    VhAgentRepo vhAgentRepo,
    ILogger<FarmTokenUpdater> logger)

{
    public async Task Update(Guid[] farmIds, bool saveChanged)
    {
        foreach (var farmId in farmIds) {
            try {
                await Update(farmId, false);
            }
            catch (Exception ex) {
                logger.LogWarning(ex, "Could not update farm token. FarmId: {FarmId}", farmId);
            }
        }

        if (saveChanged)
            await vhAgentRepo.SaveChangesAsync();
    }

    public async Task Update(Guid farmId, bool saveChanged)
    {
        var farm = await vhAgentRepo.ServerFarmGet(farmId, includeServersAndAccessPoints: true, includeCertificates: true);
        if (FarmTokenBuilder.UpdateIfChanged(farm) && saveChanged)
            await vhAgentRepo.SaveChangesAsync();
    }

}