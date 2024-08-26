using VpnHood.AccessServer.Dtos.HostOrders;
using VpnHood.AccessServer.Persistence.Models.HostOrders;

namespace VpnHood.AccessServer.DtoConverters;

public static class HostOrderConverter
{
    public static HostOrder ToDto(this HostOrderModel model)
    {
        ArgumentNullException.ThrowIfNull(model.HostProvider);
        var serverModel = model.NewIpOrderServer;

        var hostOrder = new HostOrder {
            OrderId = model.HostOrderId.ToString(),
            HostProviderId = model.HostProviderId.ToString(),
            HostProviderName = model.HostProvider.HostProviderName,
            CreatedTime = model.CreatedTime,
            OrderType = model.OrderType,
            Status = model.Status,
            ErrorMessage = model.ErrorMessage,
            CompletedTime = model.CompletedTime,
            ProviderOrderId = model.ProviderOrderId,
            NewIpOrderIpAddress = model.NewIpOrderIpAddress,
            ServerId = serverModel?.ServerId,
            ServerName = serverModel?.ServerName,
            ServerLocation = serverModel?.Location?.ToDto(),
            ServerFarmId = serverModel?.ServerFarmId,
            ServerFarmName = serverModel?.ServerFarm?.ServerFarmName,
        };

        return hostOrder;
    }
}