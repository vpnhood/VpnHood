using System.Net;
using GrayMint.Common.Utils;
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
            ProviderName = providerModel.ProviderName,
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
        using var asyncLock = await AsyncLock.LockAsync($"HostOrderSync_{projectId}", TimeSpan.Zero);
        if (!asyncLock.Succeeded)
            return false;

        // update database with new ips
        var hostIps = await AddProvidersNewIps(projectId, appOptions.Value.ServiceHttpTimeout);

        // list all pending orders
        var pendingOrders = await vhRepo.HostOrdersList(projectId, status: HostOrderStatus.Pending);

        // update pending NewIp orders
        foreach (var pendingOrder in pendingOrders.Where(x => x.OrderType == HostOrderType.NewIp)) {
            try {
                var hostIp = hostIps.FirstOrDefault(x => x.Description?.Contains(BuildOrderTag(pendingOrder.HostOrderId)) == true);
                if (hostIp == null)
                    continue;

                // update server endpoints
                if (pendingOrder.NewIpOrderServerId != null)
                    await AddIpToServer(projectId, pendingOrder.NewIpOrderServerId.Value, hostIp.IpAddress);

                pendingOrder.Status = HostOrderStatus.Completed;
                pendingOrder.CompletedTime = DateTime.UtcNow;
                pendingOrder.NewIpOrderIpAddress = hostIp.IpAddress;
            }
            catch (Exception ex) {
                logger.LogError(ex, "Error while adding ip to server. ProjectId: {ProjectId}, OrderId: {OrderId}",
                    projectId, pendingOrder.HostOrderId);
            }
        }

        // update pending ReleaseIp orders
        foreach (var pendingOrder in pendingOrders.Where(x => x.OrderType == HostOrderType.ReleaseIp)) {
            try {
                var hostIp = hostIps.FirstOrDefault(x => x.IpAddress.Equals(pendingOrder.ReleaseOrderIpAddress));
                if (hostIp?.ExistsInProvider == true)
                    continue;

                ArgumentNullException.ThrowIfNull(pendingOrder.ReleaseOrderIpAddress);
                await RemoveIpFromServer(projectId, pendingOrder.ReleaseOrderIpAddress);

                pendingOrder.Status = HostOrderStatus.Completed;
                pendingOrder.CompletedTime = DateTime.UtcNow;
                if (hostIp != null)
                    hostIp.IsDeleted = true;

            }
            catch (Exception ex) {
                logger.LogError(ex, "Error while removing ip from server. ProjectId: {ProjectId}, OrderId: {OrderId}",
                    projectId, pendingOrder.HostOrderId);
            }
        }

        await vhRepo.SaveChangesAsync();
        return pendingOrders.All(x => x.Status != HostOrderStatus.Pending);
    }

    private async Task RemoveIpFromServer(Guid projectId, IPAddress ipAddress)
    {
        logger.LogInformation("Removing hostIp from server. ProjectId: {ProjectId}, Ip: {ipAddress}",
            projectId, ipAddress);

        // find server using this ip
        var servers = await vhRepo.ServerList(projectId, tracking: false);
        var server = FindServerFromIp(servers, ipAddress);
        if (server == null) {
            logger.LogWarning(
                "Could not find any server to remove the given ip. ProjectId: {ProjectId}, Ip: {ipAddress}",
                projectId, ipAddress);
            return;
        }

        // remove ip from server and update farm
        server.AccessPoints.RemoveAll(x => x.IpAddress.Equals(ipAddress));
        await serverConfigureService.SaveChangesAndInvalidateServer(projectId, server.ServerId, true);
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
        var providerIpInfosTasks = hostProviders.Select(
            x => new {
                x.Provider,
                x.ProviderName,
                ListTask = x.Provider.ListIps(BuildProjectTag(projectId), timeout)
            }).ToArray();

        await Task.WhenAll(providerIpInfosTasks.Select(providerTask => providerTask.ListTask));

        //combine all task results into Provider and Ip list
        var providerIpInfos = providerIpInfosTasks
            .SelectMany(x => x.ListTask.Result.Select(y => new { x.Provider, x.ProviderName, IpAddress = y }))
            .ToArray();

        // Get all HostIPs from database
        var hostIPs = await vhRepo.HostIpList(projectId);

        // Add new IPs to database
        foreach (var providerIpInfo in providerIpInfos) {
            if (hostIPs.Any(x => x.IpAddress.Equals(providerIpInfo.IpAddress)))
                continue;

            var hostProviderIp = await providerIpInfo.Provider.GetIp(providerIpInfo.IpAddress, timeout);
            var hostIpModel = new HostIpModel {
                IpAddress = providerIpInfo.IpAddress,
                ProviderName = providerIpInfo.ProviderName,
                Description = hostProviderIp.Description,
                CreatedTime = DateTime.UtcNow,
                ExistsInProvider = true,
                ProjectId = projectId
            };
            await vhRepo.AddAsync(hostIpModel);

            // let save changes immediately as exception may occur in next items
            await vhRepo.SaveChangesAsync();
        }

        // update host ips which is not exists in provider
        foreach (var hostIp in hostIPs)
            hostIp.ExistsInProvider = providerIpInfos.Any(x => x.IpAddress.Equals(hostIp.IpAddress));

        await vhRepo.SaveChangesAsync();
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
    public async Task<HostIp[]> ListIps(Guid projectId, string? search = null, int recordIndex = 0, int recordCount = int.MaxValue)
    {
        var hostIps = await vhRepo.HostIpList(projectId, search: search, recordIndex: recordIndex, recordCount: recordCount);

        // get all servers
        var servers = await vhRepo.ServerList(projectId, includeServerFarm: true, tracking: false);

        // return all host ips
        return hostIps.Select(x =>
            x.ToDto(FindServerFromIp(servers, x.IpAddress))).ToArray();
    }

    public async Task<HostOrder[]> List(Guid projectId, int recordIndex = 0, int recordCount = int.MaxValue)
    {
        await Sync(projectId);
        var hostOrders = await vhRepo.HostOrdersList(projectId, recordIndex: recordIndex, recordCount: recordCount);
        return hostOrders.Select(x => x.ToDto()).ToArray();
    }

    public async Task<HostOrder> OrderReleaseIp(Guid projectId, IPAddress ipAddress, bool ignoreProviderError)
    {
        Logger.LogInformation("Releasing and IP from provider. ProjectId: {ProjectId}, Ip: {ip}", projectId, ipAddress);

        // get host ip and make sure ip exists in HostIps
        var hostIp = await vhRepo.HostIpGet(projectId, ipAddress);

        // find server that use the ip
        var servers = await vhRepo.ServerList(projectId, tracking: false);
        var server = FindServerFromIp(servers, ipAddress);
        if (server != null) {
            // remove ip from server and update farm
            server.AccessPoints.RemoveAll(x => x.IpAddress.Equals(ipAddress));
            await serverConfigureService.SaveChangesAndInvalidateServer(projectId, server.ServerId, true);
        }

        // get the provider
        var providerModel = await vhRepo.ProviderGet(projectId, providerType: ProviderType.HostOrder, providerName: hostIp.ProviderName);
        var provider = hostProviderFactory.Create(providerModel.ProviderName, providerModel.Settings);

        // create release order
        var orderId = Guid.NewGuid();
        await provider.ReleaseIp(ipAddress, appOptions.Value.ServiceHttpTimeout);

        // save order
        var hostOrderModel = new HostOrderModel {
            HostOrderId = orderId,
            ProjectId = projectId,
            ProviderName = hostIp.ProviderName,
            Status = HostOrderStatus.Pending,
            CreatedTime = DateTime.UtcNow,
            ProviderOrderId = null,
            OrderType = HostOrderType.ReleaseIp,
            ReleaseOrderIpAddress = ipAddress,
            NewIpOrderServerId = null,
        };
        await vhRepo.AddAsync(hostOrderModel);
        await vhRepo.SaveChangesAsync();

        // monitor the order
        _ = MonitorOrder(serviceScopeFactory, projectId, appOptions.Value.HostOrderMonitorCount, appOptions.Value.HostOrderMonitorInterval);
        return hostOrderModel.ToDto();
    }
}