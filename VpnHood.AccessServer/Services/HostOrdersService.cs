using System.Net;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos.HostOrders;
using VpnHood.AccessServer.Persistence.Enums;
using VpnHood.AccessServer.Persistence.Models;
using VpnHood.AccessServer.Persistence.Models.HostOrders;
using VpnHood.AccessServer.Providers.Hosts;
using VpnHood.AccessServer.Repos;
using InvalidOperationException = System.InvalidOperationException;

namespace VpnHood.AccessServer.Services;

public class HostOrdersService(
    VhRepo vhRepo,
    IHostProviderFactory hostProviderFactory)
{
    private static string GetHostProviderName(ServerModel serverModel)
    {
        if (string.IsNullOrEmpty(serverModel.HostPanelUrl))
            throw new InvalidOperationException($"{nameof(serverModel.HostPanelUrl)} must be set before using host orders.");

        return new Uri(serverModel.HostPanelUrl).Host;
    }

    public async Task<HostOrder> OrderNewIp(Guid projectId, Guid serverId)
    {
        // Get the server and provider
        var serverModel = await vhRepo.ServerGet(projectId: projectId, serverId: serverId);
        var hostProviderName = GetHostProviderName(serverModel);
        var providerModel = await vhRepo.ProviderGet(projectId, providerType: ProviderType.HostOrder, providerName: hostProviderName);


        // Create the provider
        var provider = hostProviderFactory.Create(providerModel.ProviderName, providerModel.Settings);

        // Get the server IP by current IP
        var serverIp = serverModel.GatewayIpV4 ?? serverModel.GatewayIpV6 ??
            throw new InvalidOperationException("Server does not have an IP address.");

        // order new ip
        var providerServerId = await provider.GetServerIdFromIp(IPAddress.Parse(serverIp));
        var providerOrder = await provider.OrderNewIp(providerServerId, $"#project:{projectId}");

        // save order
        var hostOrderModel = new HostOrderModel {
            OrderId = 0,
            ProjectId = projectId,
            Status = HostOrderStatus.Pending,
            CreatedTime = DateTime.UtcNow,
            ProviderOrderId = providerOrder.OrderId,
            OrderType = HostOrderType.NewIp,
        };
        await vhRepo.AddAsync(hostOrderModel);
        await vhRepo.SaveChangesAsync();

        // return the order
        return hostOrderModel.ToDto();
    }
}