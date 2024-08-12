using System.Net;
using Microsoft.Extensions.Options;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos.HostOrders;
using VpnHood.AccessServer.Options;
using VpnHood.AccessServer.Persistence.Enums;
using VpnHood.AccessServer.Persistence.Models;
using VpnHood.AccessServer.Persistence.Models.HostOrders;
using VpnHood.AccessServer.Providers.Hosts;
using VpnHood.AccessServer.Repos;

namespace VpnHood.AccessServer.Services;

public class HostOrdersService(
    VhRepo vhRepo,
    IOptions<AppOptions> appOptions,
    IHostProviderFactory hostProviderFactory,
    ILogger<HostOrdersService> logger,
    IServiceScopeFactory serviceScopeFactory,
    ServerConfigureService serverConfigureService)
{
    private ILogger<HostOrdersService> Logger => logger;

    private static string GetHostProviderName(ServerModel serverModel)
    {
        if (string.IsNullOrEmpty(serverModel.HostPanelUrl))
            throw new InvalidOperationException($"{nameof(serverModel.HostPanelUrl)} must be set before using host orders.");

        return new Uri(serverModel.HostPanelUrl).Host;
    }

    private static string BuildProjectTag(Guid projectId) => $"#Project:{projectId}";
    private static string BuildOrderTag(Guid orderId) => $"#OrderId:{orderId}";

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
            throw new InvalidOperationException("Server does not have any IP address.");

        // order new ip
        var orderId = Guid.NewGuid();
        var providerServerId = await provider.GetServerIdFromIp(IPAddress.Parse(serverIp), appOptions.Value.ServiceHttpTimeout)
            ?? throw new InvalidOperationException("Could not find the Server default gateway IP address in the provider.");

        var providerOrderId = await provider.OrderNewIp(providerServerId,
            $"{BuildProjectTag(projectId)} {BuildOrderTag(orderId)}",
            appOptions.Value.ServiceHttpTimeout);

        // save order
        var hostOrderModel = new HostOrderModel {
            HostOrderId = orderId,
            ProjectId = projectId,
            Status = HostOrderStatus.Pending,
            CreatedTime = DateTime.UtcNow,
            ProviderOrderId = providerOrderId,
            OrderType = HostOrderType.NewIp,
            NewIpOrderServerId = serverId,
        };
        await vhRepo.AddAsync(hostOrderModel);
        await vhRepo.SaveChangesAsync();

        _ = MonitorOrder(serviceScopeFactory, projectId, appOptions.Value.HostOrderMonitorCount, appOptions.Value.HostOrderMonitorInterval);

        // return the order
        return hostOrderModel.ToDto();
    }

    private static async Task MonitorOrder(IServiceScopeFactory serviceScopeFactory, Guid projectId, int count, TimeSpan delay)
    {
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var hostOrdersService = scope.ServiceProvider.GetRequiredService<HostOrdersService>();

        hostOrdersService.Logger.LogInformation("Waiting for new host ip order. ProjectId: {ProjectId}", projectId);

        // start monitoring the order
        for (var i = 0; i < count; i++) {
            if (await hostOrdersService.Sync(projectId))
                return;

            await Task.Delay(delay);
        }

        hostOrdersService.Logger.LogInformation("Order has not been finalized in given time. ProjectId: {ProjectId}", projectId);
    }

    // return true if there is not pending order left
    private async Task<bool> Sync(Guid projectId)
    {
        // update database with new ips
        var hostIps = await AddProvidersNewIps(projectId, appOptions.Value.ServiceHttpTimeout);
        await vhRepo.SaveChangesAsync();

        // list all pending orders
        var pendingOrders = await vhRepo.HostOrdersList(projectId, status: HostOrderStatus.Pending);

        // update pending orders
        foreach (var pendingOrder in pendingOrders) {
            var hostIp = hostIps.FirstOrDefault(x => x.Description?.Contains(BuildOrderTag(pendingOrder.HostOrderId)) == true);
            if (hostIp == null)
                continue;

            pendingOrder.Status = HostOrderStatus.Completed;
            pendingOrder.CompletedTime = DateTime.UtcNow;
            pendingOrder.NewIpOrderIpAddress = hostIp.IpAddress;

            // update server endpoints
            if (pendingOrder.NewIpOrderServerId != null)
                await AddIpToServer(projectId, pendingOrder.NewIpOrderServerId.Value, hostIp.IpAddress);

            // list all ips from provider
            await vhRepo.SaveChangesAsync();
        }

        return pendingOrders.All(x => x.Status != HostOrderStatus.Pending);
    }

    private async Task AddIpToServer(Guid projectId, Guid serverId, IPAddress ipAddress)
    {
        logger.LogInformation("Adding hostIp to Server. ProjectId: {ProjectId}, ServerId:{ServerId}, Ip: {Ip}",
            projectId, serverId, ipAddress);

        var server = await vhRepo.ServerGet(projectId, serverId);

        // add new ip to server if it does not exist
        if (server.AccessPoints.Any(x => x.IpAddress.Equals(ipAddress))) {
            logger.LogWarning("The Ip has been already added to the server. ProjectId: {ProjectId}, ServerId:{ServerId}, Ip: {Ip}",
                projectId, serverId, ipAddress);
            return;
        }

        // add new ip to server
        var firstIsListen = server.AccessPoints.FirstOrDefault(x => x.IsListen);
        server.AutoConfigure = false;
        server.AccessPoints.Add(new AccessPointModel {
            IpAddress = ipAddress,
            TcpPort = firstIsListen?.TcpPort ?? 443,
            UdpPort = firstIsListen?.UdpPort ?? 443,
            AccessPointMode = AccessPointMode.PublicInToken,
            IsListen = true
        });

        // save changes
        await serverConfigureService.SaveChangesAndInvalidateServer(projectId, server.ServerId, true);
    }

    private async Task<HostIpModel[]> AddProvidersNewIps(Guid projectId, TimeSpan timeout)
    {
        // create all host providers of the project
        var providerModels = await vhRepo.ProviderList(projectId, providerType: ProviderType.HostOrder);
        var hostProviders = providerModels.Select(x =>
            new { x.ProviderName, Provider = hostProviderFactory.Create(x.ProviderName, x.Settings) })
            .ToArray();

        // list all ips from provider
        var providerIpListTasks = hostProviders.Select(
            x => new {
                x.ProviderName,
                ListTask = x.Provider.LisIps(timeout)
            }).ToArray();

        await Task.WhenAll(providerIpListTasks.Select(providerTask => providerTask.ListTask));

        // combine all providers and ips
        var providerIps = providerIpListTasks.SelectMany(x => x.ListTask.Result.Select(y => new { x.ProviderName, HostIp = y }))
            .Where(x => x.HostIp.Description?.Contains(BuildProjectTag(projectId), StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();

        // Get all HostIPs from database
        var hostIPs = await vhRepo.HostIpList(projectId);

        // Add new IPs to database
        foreach (var providerIp in providerIps) {
            if (hostIPs.Any(x => x.IpAddress.Equals(providerIp.HostIp.IpAddress)))
                continue;

            var hostIpModel = new HostIpModel {
                IpAddress = providerIp.HostIp.IpAddress,
                ProviderName = providerIp.ProviderName,
                Description = providerIp.HostIp.Description,
                CreatedTime = DateTime.UtcNow,
                ExistsInProvider = true,
                ProjectId = projectId
            };
            await vhRepo.AddAsync(hostIpModel);
        }

        // update host ips which is not exists in provider
        foreach (var hostIp in hostIPs)
            hostIp.ExistsInProvider = providerIps.Any(x => x.HostIp.IpAddress.Equals(hostIp.IpAddress));

        return hostIPs;
    }

    public async Task<HostOrder> Get(Guid projectId, string orderId)
    {
        var result = await vhRepo.HostOrderGet(projectId, Guid.Parse(orderId));
        return result.ToDto();
    }

    private static ServerModel? FindServerFromIp(ServerModel[] servers, IPAddress ip)
    {
        return servers.FirstOrDefault(y => y.AccessPoints.Any(z => z.IpAddress.Equals(ip)));
    }
    public async Task<HostIp[]> ListIps(Guid projectId)
    {
        await Sync(projectId);
        var hostIps = await vhRepo.HostIpList(projectId);

        // get all servers
        var servers = await vhRepo.ServerList(projectId, includeServerFarm: true, tracking: false);

        // return all host ips
        return hostIps.Select(x =>
            x.ToDto(FindServerFromIp(servers, x.IpAddress))).ToArray();
    }

    public async Task<HostOrder[]> List(Guid projectId, int recordIndex = 0, int recordCount = int.MaxValue)
    {
        var hostOrders = await vhRepo.HostOrdersList(projectId, recordIndex: recordIndex, recordCount: recordCount);
        return hostOrders.Select(x => x.ToDto()).ToArray();
    }
}