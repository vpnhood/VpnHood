using VpnHood.AccessServer.Dtos.HostOrders;
using VpnHood.AccessServer.Persistence.Models;
using VpnHood.AccessServer.Persistence.Models.HostOrders;

namespace VpnHood.AccessServer.DtoConverters;

public static class HostIpConverter
{
    public static HostIp ToDto(this HostIpModel model, ServerModel? serverModel)
    {
        ArgumentNullException.ThrowIfNull(model.Provider);

        var hostIp = new HostIp {
            IpAddress = model.IpAddress,
            CreatedTime = model.CreatedTime,
            ProviderId = model.ProviderId.ToString(),
            ProviderName = model.Provider.ProviderName,
            ExistsInProvider = model.ExistsInProvider,
            ProviderDescription = model.Description,
            AutoReleaseTime = model.AutoReleaseTime,
            ReleaseRequestTime = model.ReleaseRequestTime,
            ServerId = serverModel?.ServerId,
            ServerName = serverModel?.ServerName,
            ServerLocation = serverModel?.Location?.ToDto(),
            ServerFarmId = serverModel?.ServerFarmId,
            ServerFarmName = serverModel?.ServerFarm?.ServerFarmName,
        };

        return hostIp;
    }
}