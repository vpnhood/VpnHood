using System.Net;
using GrayMint.Common.AspNetCore.Jobs;
using GrayMint.Common.Exceptions;
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
    : IGrayMintJob
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

    public async Task<HostOrder> CreateNewIpOrder(Guid projectId, HostOrderNewIp hostOrderNewIp)
    {
        logger.LogInformation("Creating new ip order. ProjectId: {ProjectId}, ServerId: {ServerId}, OldIp: {OldIp}, ReleaseTime: {ReleaseTime}",
            projectId, hostOrderNewIp.ServerId, hostOrderNewIp.OldIpAddress, hostOrderNewIp.OldIpAddressReleaseTime);

        // Get the server and provider
        var serverModel = await vhRepo.ServerGet(projectId: projectId, serverId: hostOrderNewIp.ServerId);
        var hostProviderName = GetHostProviderName(serverModel);
        var providerModel = await vhRepo.HostProviderGetByName(projectId, hostProviderName: hostProviderName);
        if (providerModel == null)
            throw new NotExistsException($"Could not find any host provider for this server. HostProvider: {hostProviderName}");

        // throw exception if old ipaddress does not belong to the server
        var oldHostIp = hostOrderNewIp.OldIpAddress != null ? await vhRepo.HostIpGet(projectId, hostOrderNewIp.OldIpAddress.ToString()) : null;
        if (oldHostIp != null &&
            !serverModel.AccessPoints.Any(x => x.IpAddress.Equals(hostOrderNewIp.OldIpAddress)))
            throw new InvalidOperationException("The old ip address does not belong to the server.");

        // Create the provider
        var provider = hostProviderFactory.Create(providerModel.HostProviderId, providerModel.HostProviderName, providerModel.Settings);

        // Get the server IP by current IP
        var serverIp = GetServerGatewayIp(serverModel);

        // order new ip
        var orderId = Guid.NewGuid();
        var providerServerId = await provider.GetServerIdFromIp(serverIp, appOptions.Value.ServiceHttpTimeout)
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
            HostProviderId = providerModel.HostProviderId,
            HostProvider = providerModel,
            ProviderOrderId = providerOrderId,
            OrderType = HostOrderType.NewIp,
            ErrorMessage = null,
            CompletedTime = null,
            NewIpOrderServerId = hostOrderNewIp.ServerId,
            NewIpOrderOldIpAddressReleaseTime = hostOrderNewIp.OldIpAddressReleaseTime
        };
        await vhRepo.AddAsync(hostOrderModel);

        // update old ip order id
        if (oldHostIp != null)
            oldHostIp.RenewOrderId = orderId;

        await vhRepo.SaveChangesAsync();

        _ = MonitorRequests(serviceScopeFactory, appOptions.Value.HostOrderMonitorCount, appOptions.Value.HostOrderMonitorInterval);

        // return the order
        return hostOrderModel.ToDto();
    }

    private static IPAddress GetServerGatewayIp(ServerModel server)
    {
        // extract fakeIp from server host panel url query string using HttpUtility.ParseQueryString
        if (server.HostPanelUrl?.Contains(FakeHostProvider.BaseProviderName) == true) {
            // convert serverId to ipv6
            var bytes = server.ServerId.ToByteArray();
            bytes[0] = 255;
            bytes[1] = 255;
            var fakeIp = new IPAddress(bytes);
            return fakeIp;
        }

        return IPAddress.Parse(server.GatewayIpV4 ?? server.GatewayIpV6 ??
            throw new InvalidOperationException("Server does not have any IP address."));
    }


    private static async Task MonitorRequests(IServiceScopeFactory serviceScopeFactory, int count, TimeSpan delay)
    {
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var hostOrdersService = scope.ServiceProvider.GetRequiredService<HostOrdersService>();

        // start monitoring the order
        for (var i = 0; i < count; i++) {
            if (await hostOrdersService.ProcessJobs(CancellationToken.None))
                return;

            await Task.Delay(delay);
        }

        hostOrdersService.Logger.LogInformation("Some Order has not been finalized in the given time.");
    }

    private async Task ProcessAllAutoReleaseIps()
    {
        // Process AutoReleaseDate
        var expiredHostIps = await vhRepo.HostIpListAllExpired();
        foreach (var hostIp in expiredHostIps) {
            try {
                await ReleaseIpInternal(hostIp.ProjectId, hostIp.GetIpAddress(), ignoreProviderError: false);
            }
            catch (Exception ex) {
                Logger.LogError(ex, "Error while releasing ip. ProjectId: {ProjectId}, Ip: {Ip}",
                    hostIp.ProjectId, hostIp.IpAddress);
            }
        }
    }

    private async Task ProcessAllPendingOrders()
    {
        // find pending orders and releasing hostIps 
        var pendingOrders = await vhRepo.HostOrdersList(status: HostOrderStatus.Pending);
        var releasingHostIps = await vhRepo.HostIpListReleasing();
        var projectIds = pendingOrders
            .Select(x => x.ProjectId)
            .Concat(releasingHostIps.Select(x => x.ProjectId))
            .Distinct()
            .ToArray();

        foreach (var projectId in projectIds) {
            await Sync(projectId);
        }
    }

    public Task RunJob(CancellationToken cancellationToken)
    {
        return ProcessJobs(cancellationToken);
    }

    // return true if there is not pending order left
    private async Task<bool> ProcessJobs(CancellationToken cancellationToken)
    {
        // RunJob may be called by RequestMonitorJob, so we need to lock it
        Logger.LogInformation("Start processing pending host orders.");

        using var asyncLock = await AsyncLock.LockAsync("HostOrdersService:ProcessJobs", TimeSpan.Zero, cancellationToken);
        if (!asyncLock.Succeeded)
            return false;

        await ProcessAllPendingOrders();
        await ProcessAllAutoReleaseIps();

        // check if there is any pending order left
        var pendingOrders = await vhRepo.HostOrdersList(status: HostOrderStatus.Pending);
        if (pendingOrders.Any())
            return false;

        // check if there is any releasing hostIp left
        var releasingHostIps = await vhRepo.HostIpListReleasing();
        return !releasingHostIps.Any();
    }

    // return true if there is not pending order left
    private async Task Sync(Guid projectId)
    {
        using var asyncLock = await AsyncLock.LockAsync($"HostOrderSync_{projectId}", TimeSpan.Zero);
        if (!asyncLock.Succeeded)
            return;

        // update database with new ips
        var hostIps = await SyncProvidersManagedIps(projectId, appOptions.Value.ServiceHttpTimeout);

        // list all pending orders
        var pendingOrders = await vhRepo.HostOrdersList(projectId, status: HostOrderStatus.Pending);

        // update pending NewIp orders
        foreach (var pendingOrder in pendingOrders.Where(x => x.OrderType == HostOrderType.NewIp)) {
            try {
                var hostIp = hostIps.FirstOrDefault(x => x.Description?.Contains(BuildOrderTag(pendingOrder.HostOrderId)) == true);
                if (hostIp == null)
                    continue;

                Logger.LogInformation("Adding the ordered new ip from provider. ProjectId: {ProjectId}, OrderId: {OrderId}, NewIp: {NewIp}",
                    pendingOrder.ProjectId, pendingOrder.HostOrderId, hostIp.IpAddress);

                // update server endpoints
                if (pendingOrder.NewIpOrderServerId != null)
                    await AddIpToServer(projectId, pendingOrder.NewIpOrderServerId.Value, hostIp.GetIpAddress());

                // delete old ips
                foreach (var oldHostIp in hostIps.Where(x => x.RenewOrderId == pendingOrder.HostOrderId)) {
                    oldHostIp.AutoReleaseTime = pendingOrder.NewIpOrderOldIpAddressReleaseTime ?? DateTime.UtcNow;
                    Logger.LogWarning("Old ip will be released. ProjectId: {ProjectId}, OrderId: {OrderId}, OldIp: {OldIp}",
                        projectId, pendingOrder.HostOrderId, oldHostIp.IpAddress);
                }

                pendingOrder.Status = HostOrderStatus.Completed;
                pendingOrder.CompletedTime = DateTime.UtcNow;
                pendingOrder.NewIpOrderIpAddress = hostIp.IpAddress;
                await vhRepo.SaveChangesAsync();
            }
            catch (Exception ex) {
                logger.LogError(ex, "Error in adding ip to server. ProjectId: {ProjectId}, OrderId: {OrderId}",
                    projectId, pendingOrder.HostOrderId);
            }
        }

        // update pending ReleaseIp orders
        foreach (var hostIp in hostIps.Where(x => x.ReleaseRequestTime != null)) {
            try {
                if (hostIp.ExistsInProvider)
                    continue;

                await RemoveIpFromServer(projectId, hostIp.GetIpAddress());
                hostIp.DeletedTime = DateTime.UtcNow;

                await vhRepo.SaveChangesAsync();
            }
            catch (Exception ex) {
                logger.LogError(ex, "Error while removing ip from server. ProjectId: {ProjectId}, Ip: {Ip}",
                    projectId, hostIp.IpAddress);
            }
        }
    }

    private async Task RemoveIpFromServer(Guid projectId, IPAddress ipAddress)
    {
        logger.LogInformation("Removing hostIp from server. ProjectId: {ProjectId}, Ip: {ipAddress}",
            projectId, ipAddress);

        // find server using this ip
        var servers = await vhRepo.ServerList(projectId, tracking: true);
        var server = FindServerFromIp(servers, ipAddress);
        if (server == null) {
            logger.LogWarning(
                "Could not find any server to remove the given ip. ProjectId: {ProjectId}, Ip: {ipAddress}",
                projectId, ipAddress);
            return;
        }

        // remove ip from server and update farm
        server.AccessPoints.RemoveAll(x => x.IpAddress.Equals(ipAddress));
        await serverConfigureService.SaveChangesAndInvalidateServer(projectId, server, true);
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
        await serverConfigureService.SaveChangesAndInvalidateServer(projectId, server, true);
    }

    private async Task<HostIpModel[]> SyncProvidersManagedIps(Guid projectId, TimeSpan timeout)
    {
        // create all host providers of the project
        var providerModels = await vhRepo.HostProviderList(projectId);
        var hostProviders = providerModels.Select(x =>
            new {
                ProviderId = x.HostProviderId,
                ProviderName = x.HostProviderName,
                Provider = hostProviderFactory.Create(x.HostProviderId, x.HostProviderName, x.Settings)
            })
            .ToArray();

        // list all ips from provider
        var providerIpInfosTasks = hostProviders.Select(
            x => new {
                x.ProviderId,
                x.ProviderName,
                x.Provider,
                ListTask = x.Provider.ListIps(BuildProjectTag(projectId), timeout)
            }).ToArray();

        await Task.WhenAll(providerIpInfosTasks.Select(providerTask => providerTask.ListTask));

        //combine all task results into HostProvider and Ip list
        var providerIpInfos = providerIpInfosTasks
            .SelectMany(x => x.ListTask.Result.Select(y => new {
                x.Provider, 
                x.ProviderName, 
                x.ProviderId,
                IpAddress = y
            }))
            .ToArray();

        // Get all HostIPs from database
        var hostIps = await vhRepo.HostIpList(projectId);

        // Add new IPs to database
        foreach (var providerIpInfo in providerIpInfos) {
            if (hostIps.Any(x => x.GetIpAddress().Equals(providerIpInfo.IpAddress)))
                continue;

            Logger.LogInformation("Adding a new ip from provider to db. ProjectId: {ProjectId}, HostProviderName: {HostProviderName}, Ip: {Ip}",
                projectId, providerIpInfo.ProviderName, providerIpInfo.IpAddress);

            var hostProviderIp = await providerIpInfo.Provider.GetIp(providerIpInfo.IpAddress, timeout);
            var hostIpModel = new HostIpModel {
                IpAddress = providerIpInfo.IpAddress.ToString(),
                HostProviderId = providerIpInfo.ProviderId,
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
        foreach (var hostIp in hostIps)
            hostIp.ExistsInProvider = providerIpInfos.Any(x => x.IpAddress.Equals(hostIp.GetIpAddress()));

        await vhRepo.SaveChangesAsync();
        return hostIps;
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
        var hostIpModels = await vhRepo.HostIpList(projectId, search: search, recordIndex: recordIndex, recordCount: recordCount);

        // get all servers
        var servers = await vhRepo.ServerList(projectId, includeServerFarm: true, tracking: false);

        // return all host ips
        var hostIps = hostIpModels.Select(x =>
            x.ToDto(FindServerFromIp(servers, x.GetIpAddress()))).ToArray();

        return hostIps;
    }

    public async Task<HostOrder[]> List(Guid projectId, string? search = null, int recordIndex = 0, int recordCount = int.MaxValue)
    {
        await Sync(projectId);
        var hostOrders = await vhRepo.HostOrdersList(projectId, includeServer: true, search: search,
            recordIndex: recordIndex, recordCount: recordCount);
        
        return hostOrders.Select(x => x.ToDto()).ToArray();
    }

    public async Task ReleaseIp(Guid projectId, IPAddress ipAddress, bool ignoreProviderError)
    {
        await ReleaseIpInternal(projectId, ipAddress, ignoreProviderError);
        _ = MonitorRequests(serviceScopeFactory, appOptions.Value.HostOrderMonitorCount, appOptions.Value.HostOrderMonitorInterval);
    }

    private async Task ReleaseIpInternal(Guid projectId, IPAddress ipAddress, bool ignoreProviderError)
    {
        Logger.LogInformation("Releasing an IP from provider. ProjectId: {ProjectId}, Ip: {ip}", projectId, ipAddress);

        // get host ip and make sure ip exists in HostIps
        var hostIp = await vhRepo.HostIpGet(projectId, ipAddress.ToString());

        // find server that use the ip
        var servers = await vhRepo.ServerList(projectId, tracking: true);
        var server = FindServerFromIp(servers, ipAddress);
        if (server != null) {
            // remove ip from server and update farm
            server.AccessPoints.RemoveAll(x => x.IpAddress.Equals(ipAddress));
            await serverConfigureService.SaveChangesAndInvalidateServer(projectId, server, true);
        }

        // get the provider
        var providerModel = await vhRepo.HostProviderGet(projectId,hostProviderId: hostIp.HostProviderId);
        if (providerModel == null)
            throw new NotExistsException($"Could not find any host provider for this server. HostProviderName: {hostIp.HostProvider?.HostProviderName}");

        try {
            var provider = hostProviderFactory.Create(providerModel.HostProviderId, providerModel.HostProviderName, providerModel.Settings);
            await provider.ReleaseIp(ipAddress, appOptions.Value.ServiceHttpTimeout);
        }
        catch (Exception ex) when (ignoreProviderError) {
            logger.LogWarning(ex, "Could not release ip from provider. Ip: {Ip}", ipAddress);
        }

        // create release order
        hostIp.ReleaseRequestTime = DateTime.UtcNow;
        await vhRepo.SaveChangesAsync();
    }
}