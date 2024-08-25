using VpnHood.AccessServer.Dtos.HostOrders;
using VpnHood.AccessServer.Persistence.Models.HostOrders;

namespace VpnHood.AccessServer.DtoConverters;

public static class HostOrderConverter
{
    public static HostOrder ToDto(this HostOrderModel model)
    {
        ArgumentNullException.ThrowIfNull(model.HostProvider);

        var hostOrder = new HostOrder {
            OrderId = model.HostOrderId.ToString(),
            CreatedTime = model.CreatedTime,
            OrderType = model.OrderType,
            Status = model.Status,
            ErrorMessage = model.ErrorMessage,
            CompletedTime = model.CompletedTime,
            ProviderOrderId = model.ProviderOrderId,
            NewIpOrderIpAddress = model.NewIpOrderIpAddress,
            ProviderId = model.HostProviderId.ToString(),
            ProviderName = model.HostProvider.HostProviderName,
            NewIpOrderServer = model.NewIpOrderServer?.ToDto(null)
        };

        return hostOrder;
    }
}