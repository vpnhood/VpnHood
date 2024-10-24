using System.Net;
using System.Net.Sockets;
using GrayMint.Common.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using VpnHood.AccessServer.Agent.Repos;
using VpnHood.AccessServer.Agent.Utils;
using VpnHood.AccessServer.Persistence.Caches;
using VpnHood.AccessServer.Persistence.Enums;
using VpnHood.AccessServer.Persistence.Models;
using VpnHood.Common.IpLocations;
using VpnHood.Common.Messaging;
using VpnHood.Common.Net;
using VpnHood.Common.Utils;
using VpnHood.Server.Access;
using VpnHood.Server.Access.Configurations;
using VpnHood.Server.Access.Messaging;
using SessionOptions = VpnHood.Server.Access.Configurations.SessionOptions;

namespace VpnHood.AccessServer.Agent.Services;

public class AgentService(
    ILogger<AgentService> logger,
    ILogger<AgentService.ServerStatusLogger> serverStatusLogger,
    IOptions<AgentOptions> agentOptions,
    CacheService cacheService,
    SessionService sessionService,
    [FromKeyedServices(Program.LocationProviderServer)]
    IIpLocationProvider ipLocationProvider,
    VhAgentRepo vhAgentRepo)
{
    private readonly AgentOptions _agentOptions = agentOptions.Value;

    public class ServerStatusLogger;

    public async Task<ServerCache> GetServer(Guid serverId)
    {
        var server = await cacheService.GetServer(serverId);
        return server;
    }

    public async Task<SessionResponseEx> CreateSession(Guid serverId, SessionRequestEx sessionRequestEx)
    {
        var server = await GetServer(serverId);
        return await sessionService.CreateSession(server, sessionRequestEx);
    }

    [HttpGet("sessions/{sessionId}")]
    public async Task<SessionResponseEx> GetSession(Guid serverId, uint sessionId, string hostEndPoint,
        string? clientIp)
    {
        var server = await GetServer(serverId);
        return await sessionService.GetSession(server, sessionId, hostEndPoint, clientIp);
    }

    [HttpPost("sessions/{sessionId}/usage")]
    public async Task<SessionResponse> AddSessionUsage(Guid serverId, uint sessionId, bool closeSession,
        Traffic traffic, string? adData)
    {
        var server = await GetServer(serverId);
        return await sessionService.AddUsage(server, sessionId, traffic, closeSession, adData);
    }

    private async Task CheckServerVersion(ServerCache server, string? version)
    {
        if (!string.IsNullOrEmpty(version) && Version.Parse(version) >= AgentOptions.MinServerVersion)
            return;

        var errorMessage =
            $"Your server version is not supported. Please update your server. MinSupportedVersion: {AgentOptions.MinServerVersion}";
        if (server.LastConfigError != errorMessage) {
            // update db & cache
            var serverModel = await vhAgentRepo.FindServerAsync(server.ServerId) ??
                              throw new KeyNotFoundException($"Could not find Server! ServerId: {server.ServerId}");
            serverModel.LastConfigError = errorMessage;
            await vhAgentRepo.SaveChangesAsync();
            await cacheService.InvalidateServer(serverModel.ServerId);
        }

        logger.LogInformation("OldServer. ServerId: {ServerId}, Version: {Version}", server.ServerId, version);
        throw new NotSupportedException(errorMessage);
    }

    [HttpPost("status")]
    public async Task<ServerCommand> UpdateServerStatus(Guid serverId, ServerStatus serverStatus)
    {
        var server = await GetServer(serverId);

        // check version
        await CheckServerVersion(server, server.Version);

        // update status
        serverStatusLogger.LogInformation("Updating server status. ServerId: {ServerId}, SessionCount: {SessionCount}",
            server.ServerId, serverStatus.SessionCount);
        UpdateServerStatus(server, serverStatus, false);

        // remove LastConfigCode if server send its status
        if (server.LastConfigCode?.ToString() != serverStatus.ConfigCode ||
            server.LastConfigError != serverStatus.ConfigError) {
            logger.LogInformation("Updating a server's LastConfigCode. ServerId: {ServerId}, ConfigCode: {ConfigCode}",
                server.ServerId, serverStatus.ConfigCode);

            // update db & cache
            var serverUpdate = await vhAgentRepo.FindServerAsync(server.ServerId) ??
                               throw new KeyNotFoundException($"Could not find Server! ServerId: {server.ServerId}");
            serverUpdate.LastConfigError = serverStatus.ConfigError;
            serverUpdate.LastConfigCode = !string.IsNullOrEmpty(serverStatus.ConfigCode)
                ? Guid.Parse(serverStatus.ConfigCode)
                : null;
            serverUpdate.ConfigureTime = DateTime.UtcNow;
            await vhAgentRepo.SaveChangesAsync();
            await cacheService.InvalidateServer(serverUpdate.ServerId);
        }

        var ret = new ServerCommand(server.ConfigCode.ToString());
        return ret;
    }

    [HttpPost("configure")]
    public async Task<ServerConfig> ConfigureServer(Guid serverId, ServerInfo serverInfo)
    {
        // first use cache make sure not use db for old versions
        var server = await GetServer(serverId);
        logger.LogInformation("Configuring a Server. ServerId: {ServerId}, Version: {Version}",
            server.ServerId, serverInfo.Version);

        // check version
        await CheckServerVersion(server, serverInfo.Version.ToString());
        UpdateServerStatus(server, serverInfo.Status, true);

        // ready for update
        var serverFarmModel = await vhAgentRepo.ServerFarmGet(server.ServerFarmId,
            includeServersAndAccessPoints: true, includeCertificates: true);
        var serverModel = serverFarmModel.Servers!.Single(x => x.ServerId == serverId);

        // update cache
        var publicIpV4 = serverInfo.PublicIpAddresses.FirstOrDefault(x => x.IsV4());
        var publicIpV6 = serverInfo.PublicIpAddresses.FirstOrDefault(x => x.IsV6());
        serverModel.EnvironmentVersion = serverInfo.EnvironmentVersion.ToString();
        serverModel.OsInfo = serverInfo.OsInfo;
        serverModel.MachineName = serverInfo.MachineName;
        serverModel.ConfigureTime = DateTime.UtcNow;
        serverModel.TotalMemory = serverInfo.TotalMemory ?? 0;
        serverModel.TotalSwapMemoryMb = serverInfo.TotalSwapMemory != null ? (int?)(serverInfo.TotalSwapMemory / VhUtil.Megabytes) : null;
        serverModel.LogicalCoreCount = serverInfo.LogicalCoreCount;
        serverModel.Version = serverInfo.Version.ToString();
        serverModel.PublicIpV4 = publicIpV4?.ToString();
        serverModel.PublicIpV6 = publicIpV6?.ToString();
        serverModel.Location ??= await GetIpLocation(publicIpV4 ?? publicIpV6, CancellationToken.None);

        // calculate access points
        if (serverModel.AutoConfigure)
            serverModel.AccessPoints = BuildServerAccessPoints(serverModel.ServerId, serverFarmModel.Servers!, serverInfo);

        // update if there is any change & update cache
        await vhAgentRepo.SaveChangesAsync();
        await cacheService.InvalidateServer(server.ServerId);

        // update cache
        var serverConfig = GetServerConfig(serverModel, serverFarmModel);
        return serverConfig;
    }

    private async Task<LocationModel?> GetIpLocation(IPAddress? ipAddress, CancellationToken cancellationToken)
    {
        if (ipAddress == null)
            return null;

        try {
            var ipLocation = await ipLocationProvider.GetLocation(ipAddress, cancellationToken);
            var location =
                await vhAgentRepo.LocationFind(ipLocation.CountryCode, ipLocation.RegionName, ipLocation.CityName);

            if (location == null) {
                location = new LocationModel {
                    LocationId = 0,
                    CountryCode = ipLocation.CountryCode,
                    CountryName = ipLocation.CountryName,
                    RegionName = ipLocation.RegionName,
                    CityName = ipLocation.CityName,
                    ContinentCode = null
                };
                location = await vhAgentRepo.LocationAdd(location);
            }

            return location;
        }
        catch (Exception ex) {
            logger.LogError(ex, "Could not retrieve IP location. IP: {IP}", VhUtil.RedactIpAddress(ipAddress));
            return null;
        }
    }

    private ServerConfig GetServerConfig(ServerModel serverModel,
        ServerFarmModel serverFarmModel)
    {
        ArgumentNullException.ThrowIfNull(serverFarmModel.ServerProfile);
        ArgumentNullException.ThrowIfNull(serverFarmModel.Certificates);

        var tcpEndPoints = serverModel.AccessPoints
            .Where(accessPoint => accessPoint.IsListen)
            .Select(accessPoint => new IPEndPoint(accessPoint.IpAddress, accessPoint.TcpPort))
            .ToArray();

        var udpEndPoints = serverModel.AccessPoints
            .Where(accessPoint => accessPoint is { IsListen: true, UdpPort: > 0 })
            .Select(accessPoint => new IPEndPoint(accessPoint.IpAddress, accessPoint.UdpPort))
            .ToArray();

        // defaults
        var serverConfig = new ServerConfig {
            TrackingOptions = new TrackingOptions {
                TrackTcp = true,
                TrackUdp = true,
                TrackIcmp = true,
                TrackClientIp = true,
                TrackLocalPort = true,
                TrackDestinationPort = true,
                TrackDestinationIp = true
            },
            SessionOptions = new SessionOptions {
                TcpBufferSize = AgentUtils.GetBestTcpBufferSize(serverModel.TotalMemory)
            },
            ServerSecret = serverFarmModel.Secret,
            Certificates = serverFarmModel.Certificates
                .OrderByDescending(x => x.IsInToken)
                .Select(x => new CertificateData {
                    CommonName = x.CommonName,
                    RawData = x.RawData
                })
                .ToArray()
        };

        // merge with profile
        var serverProfileConfigJson = serverFarmModel.ServerProfile.ServerConfig;
        if (!string.IsNullOrEmpty(serverProfileConfigJson)) {
            try {
                var serverProfileConfig = GmUtil.JsonDeserialize<ServerConfig>(serverProfileConfigJson);
                serverConfig.Merge(serverProfileConfig);
            }
            catch (Exception ex) {
                logger.LogError(ex, "Could not deserialize ServerProfile's ServerConfig.");
            }
        }

        // enforced items
        serverConfig.Merge(new ServerConfig {
            TcpEndPoints = tcpEndPoints,
            UdpEndPoints = udpEndPoints,
            AddListenerIpsToNetwork = serverModel.AutoConfigure ? null : "*",
            UpdateStatusInterval = _agentOptions.ServerUpdateStatusInterval,
            SwapMemorySizeMb = serverModel.ConfigSwapMemorySizeMb,
            SessionOptions = new SessionOptions {
                Timeout = _agentOptions.SessionTemporaryTimeout,
                SyncInterval = _agentOptions.SessionSyncInterval,
                SyncCacheSize = _agentOptions.SyncCacheSize,
            }
        });

        // renew certificate
        var certificate = serverFarmModel.GetCertificateInToken();
        if (certificate.ValidateInprogress)
            serverConfig.DnsChallenge = new DnsChallenge {
                Token = certificate.ValidateToken ?? "",
                KeyAuthorization = certificate.ValidateKeyAuthorization ?? ""
            };

        serverConfig.ConfigCode = serverModel.ConfigCode.ToString(); // merge does not apply this

        return serverConfig;
    }

    private static int GetBestUdpPort(IReadOnlyCollection<AccessPointModel> oldAccessPoints,
        IPAddress ipAddress, int udpPortV4, int udpPortV6)
    {
        // find previous value
        var res = oldAccessPoints.FirstOrDefault(x => x.IpAddress.Equals(ipAddress))?.UdpPort;
        if (res != null && res != 0)
            return res.Value;

        // find from other previous ip of same family
        res = oldAccessPoints.FirstOrDefault(x => x.IpAddress.AddressFamily.Equals(ipAddress.AddressFamily))?.UdpPort;
        if (res != null && res != 0)
            return res.Value;

        // use preferred value
        var preferredValue = ipAddress.AddressFamily == AddressFamily.InterNetworkV6 ? udpPortV6 : udpPortV4;
        return preferredValue;
    }

    private static IEnumerable<IPAddress> GetMissedServerPublicIps(
        IEnumerable<AccessPointModel> oldAccessPoints,
        ServerInfo serverInfo,
        AddressFamily addressFamily)
    {
        if (serverInfo.PrivateIpAddresses.All(x =>
                x.AddressFamily != addressFamily) || // there is no private IP anymore
            serverInfo.PublicIpAddresses.Any(x =>
                x.AddressFamily == addressFamily)) // there is no problem because server could report its public IP
            return Array.Empty<IPAddress>();

        return oldAccessPoints
            .Where(x => x.IsPublic && x.IpAddress.AddressFamily == addressFamily)
            .Select(x => x.IpAddress);
    }

    private static List<AccessPointModel> BuildServerAccessPoints(Guid serverId, ICollection<ServerModel> farmServers, ServerInfo serverInfo)
    {
        // all old PublicInToken AccessPoints in the same farm
        var oldTokenAccessPoints = farmServers
            .SelectMany(serverModel => serverModel.AccessPoints)
            .Where(accessPoint => accessPoint.AccessPointMode == AccessPointMode.PublicInToken)
            .ToArray();

        // server
        var server = farmServers.Single(x => x.ServerId == serverId);

        // prepare server addresses
        var privateIpAddresses = serverInfo.PrivateIpAddresses;
        var publicIpAddresses = serverInfo.PublicIpAddresses.ToList();
        publicIpAddresses.AddRange(
            GetMissedServerPublicIps(server.AccessPoints, serverInfo, AddressFamily.InterNetwork));
        publicIpAddresses.AddRange(GetMissedServerPublicIps(server.AccessPoints, serverInfo,
            AddressFamily.InterNetworkV6));

        // create private addresses
        var accessPoints = privateIpAddresses
            .Distinct()
            .Where(ipAddress => !publicIpAddresses.Any(x => x.Equals(ipAddress)))
            .Select(ipAddress => new AccessPointModel {
                AccessPointMode = AccessPointMode.Private,
                IsListen = true,
                IpAddress = ipAddress,
                TcpPort = 443,
                UdpPort = GetBestUdpPort(oldTokenAccessPoints, ipAddress, serverInfo.FreeUdpPortV4,
                    serverInfo.FreeUdpPortV6)
            })
            .ToList();

        // create public addresses and try to save last publicInToken state
        accessPoints
            .AddRange(publicIpAddresses
                .Distinct()
                .Select(ipAddress => new AccessPointModel {
                    AccessPointMode = oldTokenAccessPoints.Any(x => x.IpAddress.Equals(ipAddress))
                        ? AccessPointMode.PublicInToken // prefer last value
                        : AccessPointMode.Public,
                    IsListen = privateIpAddresses.Any(x => x.Equals(ipAddress)),
                    IpAddress = ipAddress,
                    TcpPort = 443,
                    UdpPort = GetBestUdpPort(oldTokenAccessPoints, ipAddress, serverInfo.FreeUdpPortV4,
                        serverInfo.FreeUdpPortV6)
                }));

        // has other server in the farm offer any PublicInToken
        var hasOtherServerOwnPublicToken = farmServers.Any(x =>
            x.ServerId != serverId &&
            x.AccessPoints.Any(accessPoint => accessPoint.AccessPointMode == AccessPointMode.PublicInToken));

        // make sure at least one PublicInToken is selected
        if (!hasOtherServerOwnPublicToken) {
            SelectAccessPointAsPublicInToken(accessPoints, AddressFamily.InterNetwork);
            SelectAccessPointAsPublicInToken(accessPoints, AddressFamily.InterNetworkV6);
        }

        return accessPoints.ToList();
    }

    private static void SelectAccessPointAsPublicInToken(ICollection<AccessPointModel> accessPoints,
        AddressFamily addressFamily)
    {
        // return if already selected
        if (accessPoints.Any(x => x.AccessPointMode == AccessPointMode.PublicInToken && x.IpAddress.AddressFamily == addressFamily))
            return;

        // select first public as PublicInToken
        var firstPublic = accessPoints.FirstOrDefault(x =>
            x.AccessPointMode == AccessPointMode.Public &&
            x.IpAddress.AddressFamily == addressFamily);

        if (firstPublic == null)
            return; // not public found to select as PublicInToken

        accessPoints.Remove(firstPublic);
        accessPoints.Add(new AccessPointModel {
            AccessPointMode = AccessPointMode.PublicInToken,
            IsListen = firstPublic.IsListen,
            IpAddress = firstPublic.IpAddress,
            TcpPort = firstPublic.TcpPort,
            UdpPort = firstPublic.UdpPort
        });
    }

    private static void UpdateServerStatus(ServerCache server, ServerStatus serverStatus, bool isConfigure)
    {
        server.ServerStatus = new ServerStatusModel {
            ServerStatusId = 0,
            ProjectId = server.ProjectId,
            ServerId = server.ServerId,
            IsConfigure = isConfigure,
            IsLast = true,
            CreatedTime = DateTime.UtcNow,
            AvailableMemory = serverStatus.AvailableMemory,
            AvailableSwapMemoryMb = serverStatus.AvailableSwapMemory / VhUtil.Megabytes,
            CpuUsage = (byte?)serverStatus.CpuUsage,
            TcpConnectionCount = serverStatus.TcpConnectionCount,
            UdpConnectionCount = serverStatus.UdpConnectionCount,
            SessionCount = serverStatus.SessionCount,
            ThreadCount = serverStatus.ThreadCount,
            TunnelSendSpeed = serverStatus.TunnelSpeed.Sent,
            TunnelReceiveSpeed = serverStatus.TunnelSpeed.Received
        };
    }
}