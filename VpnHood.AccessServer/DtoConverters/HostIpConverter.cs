using System.Net;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Dtos.HostOrders;
using VpnHood.AccessServer.Persistence.Models;
using VpnHood.AccessServer.Persistence.Models.HostOrders;

namespace VpnHood.AccessServer.DtoConverters;

public static class HostIpConverter
{
    public static HostIp ToDto(this HostIpModel model, ServerModel? serverModel)
    {
        ArgumentNullException.ThrowIfNull(model.HostProvider);

        var hostIp = new HostIp {
            IpAddress = IPAddress.Parse(model.IpAddress),
            CreatedTime = model.CreatedTime,
            ProviderId = model.HostProviderId.ToString(),
            ProviderName = model.HostProvider.HostProviderName,
            IsAdditional = model.IsAdditional,
            IsHidden = model.IsHidden,
            ExistsInProvider = model.ExistsInProvider,
            AutoReleaseTime = model.AutoReleaseTime,
            ReleaseRequestTime = model.ReleaseRequestTime,
            ProviderServerFarmId = model.ProviderServerId,
            ServerLocation = GetLocation(model),
            ServerId = serverModel?.ServerId,
            ServerName = serverModel?.ServerName,
            ServerFarmId = serverModel?.ServerFarmId,
            ServerFarmName = serverModel?.ServerFarm?.ServerFarmName,
            Status = GetHostIpStatus(model, serverModel),
            ProviderDescription = model.ProviderDescription,
            Description = model.Description
        };

        return hostIp;
    }

    private static Location? GetLocation(HostIpModel hostIpModel)
    {
        if (hostIpModel.LocationCountry == null)
            return null;

        return new Location {
            CountryCode = hostIpModel.LocationCountry,
            RegionName = hostIpModel.LocationRegion,
            CityName = null
        };
    }

    private static HostIpStatus GetHostIpStatus(HostIpModel hostIpModel, ServerModel? serverModel)
    {
        if (hostIpModel.ReleaseRequestTime != null)
            return HostIpStatus.Releasing;

        if (!hostIpModel.ExistsInProvider)
            return HostIpStatus.NotInProvider;

        return serverModel != null ? HostIpStatus.InUse : HostIpStatus.NotInUse;
    }

}