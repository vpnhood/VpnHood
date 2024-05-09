using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos.Regions;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Persistence.Models;

namespace VpnHood.AccessServer.Services;

public class RegionService(VhRepo vhRepo)
{
    public async Task<Region> Create(Guid projectId, RegionCreateParams regionCreateParams)
    {
        var model = new RegionModel
        {
            ProjectId = projectId,
            RegionId = await vhRepo.RegionMaxId(projectId),
            RegionName = regionCreateParams.RegionName,
            CountryCode = regionCreateParams.CountryCode,
        };

        model = await vhRepo.AddAsync(model);
        await vhRepo.SaveChangesAsync();
        return model.ToDto();
    }


    public async Task<Region> Get(Guid projectId, int regionId)
    {
        var model = await vhRepo.RegionGet(projectId, regionId);
        return model.ToDto();
    }

    public async Task<Region> Update(Guid projectId, int regionId, RegionUpdateParams regionUpdateParams)
    {
        var model = await vhRepo.RegionGet(projectId, regionId);
        if (regionUpdateParams.RegionName!=null) model.RegionName = regionUpdateParams.RegionName;
        if (regionUpdateParams.CountryCode!=null) model.CountryCode = regionUpdateParams.CountryCode;
        await vhRepo.SaveChangesAsync();
        return model.ToDto();
    }

    public async Task<Region[]> List(Guid projectId)
    {
        var models = await vhRepo.RegionList(projectId);
        var ret = models.Select(x => x.ToDto());
        return ret.ToArray();
    }

    public async Task Delete(Guid projectId, int regionId)
    {
        await vhRepo.RegionDelete(projectId, regionId);
        await vhRepo.SaveChangesAsync();
    }
}