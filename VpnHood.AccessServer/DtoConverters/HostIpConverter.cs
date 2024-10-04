using System.Net;
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
            ExistsInProvider = model.ExistsInProvider,
            ProviderDescription = model.Description,
            AutoReleaseTime = model.AutoReleaseTime,
            ReleaseRequestTime = model.ReleaseRequestTime,
            ServerId = serverModel?.ServerId,
            ServerName = serverModel?.ServerName,
            ServerLocation = serverModel?.Location?.ToDto(),
            ServerFarmId = serverModel?.ServerFarmId,
            ServerFarmName = serverModel?.ServerFarm?.ServerFarmName,
            Status = GetHostIpStatus(model, serverModel)
        };

        return hostIp;
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