using System.Globalization;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos.Regions;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Persistence.Models;

namespace VpnHood.AccessServer.Services;

public class RegionService(VhRepo vhRepo)
{
    public async Task<RegionData> Create(Guid projectId, RegionCreateParams regionCreateParams)
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
        var ret = new RegionData
        {
            Region = model.ToDto()
        };
        return ret;
    }


    public async Task<RegionData> Get(Guid projectId, int regionId)
    {
        var model = await vhRepo.RegionGet(projectId, regionId);
        var ret = new RegionData
        {
            Region = model.ToDto()
        };
        return ret;
    }

    public async Task<RegionData> Update(Guid projectId, int regionId, RegionUpdateParams regionUpdateParams)
    {
        var model = await vhRepo.RegionGet(projectId, regionId);
        if (regionUpdateParams.RegionName!=null) model.RegionName = regionUpdateParams.RegionName;
        if (regionUpdateParams.CountryCode!=null) model.CountryCode = regionUpdateParams.CountryCode;
        await vhRepo.SaveChangesAsync();

        var ret = new RegionData
        {
            Region = model.ToDto()
        };
        return ret;
    }

    public async Task<RegionData[]> List(Guid projectId)
    {
        var models = await vhRepo.RegionList(projectId);
        var ret = models.Select(x => new RegionData
        {
            Region = x.ToDto()
        });
        return ret.ToArray();
    }

    public async Task Delete(Guid projectId, int regionId)
    {
        await vhRepo.RegionDelete(projectId, regionId);
        await vhRepo.SaveChangesAsync();
    }

    public static CountryInfo[] ListAllCountries()
    {
        // Get all specific cultures
        var countryInfos = new Dictionary<string, CountryInfo>();
        var cultures = CultureInfo.GetCultures(CultureTypes.SpecificCultures);
        foreach (var culture in cultures)
        {
            try
            {
                var regionInfo = new RegionInfo(culture.Name);
                countryInfos.TryAdd(culture.Name, new CountryInfo
                {
                    CountryCode = regionInfo.Name,
                    EnglishName = regionInfo.EnglishName
                });
            }
            catch
            {
                // ignored
            }
        }

        var items = countryInfos.Values
            .ToArray()
            .OrderBy(x => x.EnglishName);

        return items.ToArray();
    }
}