using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Utils;
using VpnHood.Common.Messaging;
using VpnHood.Common.Utils;
using VpnHood.Server.Access;
using VpnHood.Server.Access.Configurations;
using VpnHood.Server.Access.Messaging;
using SessionOptions = VpnHood.Server.Access.Configurations.SessionOptions;

namespace VpnHood.AccessServer.Agent.Services;

public class AgentService(
    ILogger<AgentService> logger,
    IOptions<AgentOptions> agentOptions,
    CacheService cacheService,
    SessionService sessionService,
    VhRepo vhRepo,
    VhContext vhContext)
{
    private readonly AgentOptions _agentOptions = agentOptions.Value;

    public async Task<ServerModel> GetServer(Guid serverId)
    {
        var server = await cacheService.GetServer(serverId) ?? throw new Exception("Could not find server.");
        return server;
    }

    public async Task<SessionResponseEx> CreateSession(Guid serverId, SessionRequestEx sessionRequestEx)
    {
        var server = await GetServer(serverId);
        return await sessionService.CreateSession(server, sessionRequestEx);
    }

    [HttpGet("sessions/{sessionId}")]
    public async Task<SessionResponseEx> GetSession(Guid serverId, uint sessionId, string hostEndPoint, string? clientIp)
    {
        var server = await GetServer(serverId);
        return await sessionService.GetSession(server, sessionId, hostEndPoint, clientIp);
    }

    [HttpPost("sessions/{sessionId}/usage")]
    public async Task<SessionResponseBase> AddSessionUsage(Guid serverId, uint sessionId, bool closeSession, Traffic traffic)
    {
        var server = await GetServer(serverId);
        return await sessionService.AddUsage(server, sessionId, traffic, closeSession);
    }

    [HttpGet("certificates/{hostEndPoint}")]
    public async Task<byte[]> GetCertificate(Guid serverId, string hostEndPoint)
    {
        var server = await GetServer(serverId);
        logger.LogInformation(AccessEventId.Server, "Get certificate. ServerId: {ServerId}, HostEndPoint: {HostEndPoint}",
            server.ServerId, hostEndPoint);

        var serverFarm = await vhContext.ServerFarms
            .Include(serverFarm => serverFarm.Certificate)
            .Where(serverFarm => !serverFarm.IsDeleted)
            .SingleAsync(serverFarm => serverFarm.ServerFarmId == server.ServerFarmId);

        return serverFarm.Certificate!.RawData;
    }

    private async Task CheckServerVersion(ServerModel server)
    {
        if (!string.IsNullOrEmpty(server.Version) && Version.Parse(server.Version) >= ServerUtil.MinServerVersion)
            return;

        var errorMessage = $"Your server version is not supported. Please update your server. MinSupportedVersion: {ServerUtil.MinServerVersion}";
        if (server.LastConfigError != errorMessage)
        {
            // update cache
            server.LastConfigError = errorMessage;
            await cacheService.UpdateServer(server);

            // update db
            var serverUpdate = await vhContext.Servers.FindAsync(server.ServerId) ?? throw new KeyNotFoundException($"Could not find Server! ServerId: {server.ServerId}");
            serverUpdate.LastConfigError = server.LastConfigError;
            await vhContext.SaveChangesAsync();

        }
        throw new NotSupportedException(errorMessage);
    }

    [HttpPost("status")]
    public async Task<ServerCommand> UpdateServerStatus(Guid serverId, ServerStatus serverStatus)
    {
        var server = await GetServer(serverId);
        await CheckServerVersion(server);
        SetServerStatus(server, serverStatus, false);

        // remove LastConfigCode if server send its status
        if (server.LastConfigCode?.ToString() != serverStatus.ConfigCode || server.LastConfigError != serverStatus.ConfigError)
        {
            logger.LogInformation(AccessEventId.Server,
                "Updating a server's LastConfigCode. ServerId: {ServerId}, ConfigCode: {ConfigCode}",
                server.ServerId, serverStatus.ConfigCode);

            // update cache
            server.ConfigureTime = DateTime.UtcNow;
            server.LastConfigError = serverStatus.ConfigError;
            server.LastConfigCode = !string.IsNullOrEmpty(serverStatus.ConfigCode) ? Guid.Parse(serverStatus.ConfigCode) : null;
            await cacheService.UpdateServer(server);

            // update db
            var serverUpdate = await vhContext.Servers.FindAsync(server.ServerId) ?? throw new KeyNotFoundException($"Could not find Server! ServerId: {server.ServerId}");
            serverUpdate.LastConfigError = server.LastConfigError;
            serverUpdate.LastConfigCode = server.LastConfigCode;
            serverUpdate.ConfigureTime = server.ConfigureTime;
            await vhContext.SaveChangesAsync();
        }

        var ret = new ServerCommand(server.ConfigCode.ToString());
        return ret;
    }

    [HttpPost("configure")]
    public async Task<ServerConfig> ConfigureServer(Guid serverId, ServerInfo serverInfo)
    {
        // first use cache make sure not use db for old versions
        var server = await GetServer(serverId);
        logger.Log(LogLevel.Information, AccessEventId.Server,
            "Configuring a Server. ServerId: {ServerId}, Version: {Version}",
            server.ServerId, serverInfo.Version);

        // check version
        server.Version = serverInfo.Version.ToString();
        await CheckServerVersion(server); // must after assigning version 

        // ready for update
        server = await vhRepo.GetServer(server.ProjectId, serverId);

        // update cache
        server.EnvironmentVersion = serverInfo.EnvironmentVersion.ToString();
        server.OsInfo = serverInfo.OsInfo;
        server.MachineName = serverInfo.MachineName;
        server.ConfigureTime = DateTime.UtcNow;
        server.TotalMemory = serverInfo.TotalMemory ?? 0;
        server.LogicalCoreCount = serverInfo.LogicalCoreCount;
        server.Version = serverInfo.Version.ToString();
        server.LastConfigError = server.LastConfigError;
        server.ConfigureTime = server.ConfigureTime;

        // calculate access points
        if (server.AutoConfigure)
        {
            var serverFarm = await vhRepo.GetServerFarm(server.ProjectId, server.ServerFarmId, true, true);
            var accessPoints = BuildServerAccessPoints(server.ServerId, serverFarm.Servers!, serverInfo);

            // check if access points has been changed, then update host token and access points
            if (JsonSerializer.Serialize(accessPoints) != JsonSerializer.Serialize(server.AccessPoints))
            {
                server.AccessPoints = accessPoints;
                AccessUtil.FarmTokenUpdateIfChanged(serverFarm);
            }
        }

        // update if there is any change
        await vhRepo.SaveChangesAsync();

        // update cache
        await cacheService.InvalidateServer(server.ServerId);

        // get object from cache and update it
        server = await GetServer(serverId);
        SetServerStatus(server, serverInfo.Status, true);

        // return configuration
        var serverConfig = GetServerConfig(server);
        return serverConfig;
    }

    private ServerConfig GetServerConfig(ServerModel server)
    {
        if (server.ServerFarm == null)
            throw new Exception("ServerFarm has not been fetched.");

        var tcpEndPoints = server.AccessPoints
            .Where(accessPoint => accessPoint.IsListen)
            .Select(accessPoint => new IPEndPoint(accessPoint.IpAddress, accessPoint.TcpPort))
            .ToArray();

        var udpEndPoints = server.AccessPoints
            .Where(accessPoint => accessPoint is { IsListen: true, UdpPort: > 0 })
            .Select(accessPoint => new IPEndPoint(accessPoint.IpAddress, accessPoint.UdpPort))
            .ToArray();

        // defaults
        var serverConfig = new ServerConfig
        {
            TrackingOptions = new TrackingOptions
            {
                TrackTcp = true,
                TrackUdp = true,
                TrackIcmp = true,
                TrackClientIp = true,
                TrackLocalPort = true,
                TrackDestinationPort = true,
                TrackDestinationIp = true
            },
            SessionOptions = new SessionOptions
            {
                TcpBufferSize = ServerUtil.GetBestTcpBufferSize(server.TotalMemory),
            },
            ServerSecret = server.ServerFarm.Secret,
            ServerTokenUrl = server.ServerFarm.TokenUrl
        };

        // merge with profile
        var serverProfileConfigJson = server.ServerFarm?.ServerProfile?.ServerConfig;
        if (!string.IsNullOrEmpty(serverProfileConfigJson))
        {
            try
            {
                var serverProfileConfig = VhUtil.JsonDeserialize<ServerConfig>(serverProfileConfigJson);
                serverConfig.Merge(serverProfileConfig);
            }
            catch (Exception ex)
            {
                logger.LogError(AccessEventId.Server, ex, "Could not deserialize ServerProfile's ServerConfig.");
            }
        }

        // enforced items
        serverConfig.Merge(new ServerConfig
        {
            TcpEndPoints = tcpEndPoints,
            UdpEndPoints = udpEndPoints,
            UpdateStatusInterval = _agentOptions.ServerUpdateStatusInterval,
            SessionOptions = new SessionOptions
            {
                Timeout = _agentOptions.SessionTemporaryTimeout,
                SyncInterval = _agentOptions.SessionSyncInterval,
                SyncCacheSize = _agentOptions.SyncCacheSize
            }
        });
        serverConfig.ConfigCode = server.ConfigCode.ToString(); // merge does not apply this

        // old version does not support null values
        if (Version.Parse(server.Version!) < new Version(2, 7, 355))
            serverConfig.ApplyDefaults();

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
        if (serverInfo.PrivateIpAddresses.All(x => x.AddressFamily != addressFamily) || // there is no private IP anymore
            serverInfo.PublicIpAddresses.Any(x => x.AddressFamily == addressFamily)) // there is no problem because server could report its public IP
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
            .ToList();

        // server
        var server = farmServers.Single(x => x.ServerId == serverId);

        // prepare server addresses
        var privateIpAddresses = serverInfo.PrivateIpAddresses;
        var publicIpAddresses = serverInfo.PublicIpAddresses.ToList();
        publicIpAddresses.AddRange(GetMissedServerPublicIps(server.AccessPoints, serverInfo, AddressFamily.InterNetwork));
        publicIpAddresses.AddRange(GetMissedServerPublicIps(server.AccessPoints, serverInfo, AddressFamily.InterNetworkV6));

        // create private addresses
        var accessPoints = privateIpAddresses
            .Distinct()
            .Where(ipAddress => !publicIpAddresses.Any(x => x.Equals(ipAddress)))
            .Select(ipAddress => new AccessPointModel
            {
                AccessPointMode = AccessPointMode.Private,
                IsListen = true,
                IpAddress = ipAddress,
                TcpPort = 443,
                UdpPort = GetBestUdpPort(oldTokenAccessPoints, ipAddress, serverInfo.FreeUdpPortV4, serverInfo.FreeUdpPortV6)
            })
            .ToList();

        // create public addresses and try to save last publicInToken state
        accessPoints
            .AddRange(publicIpAddresses
            .Distinct()
            .Select(ipAddress => new AccessPointModel
            {
                AccessPointMode = oldTokenAccessPoints.Any(x => x.IpAddress.Equals(ipAddress))
                    ? AccessPointMode.PublicInToken // prefer last value
                    : AccessPointMode.Public,
                IsListen = privateIpAddresses.Any(x => x.Equals(ipAddress)),
                IpAddress = ipAddress,
                TcpPort = 443,
                UdpPort = GetBestUdpPort(oldTokenAccessPoints, ipAddress, serverInfo.FreeUdpPortV4, serverInfo.FreeUdpPortV6)
            }));

        // has other server in the farm offer any PublicInToken
        var hasOtherServerOwnPublicToken = farmServers.Any(x =>
            x.ServerId != serverId &&
            x.AccessPoints.Any(accessPoint => accessPoint.AccessPointMode == AccessPointMode.PublicInToken));

        // make sure at least one PublicInToken is selected
        if (!hasOtherServerOwnPublicToken)
        {
            SelectAccessPointAsPublicInToken(accessPoints, AddressFamily.InterNetwork);
            SelectAccessPointAsPublicInToken(accessPoints, AddressFamily.InterNetworkV6);
        }

        return accessPoints.ToList();
    }
    private static void SelectAccessPointAsPublicInToken(ICollection<AccessPointModel> accessPoints, AddressFamily addressFamily)
    {
        if (accessPoints.Any(x => x.AccessPointMode == AccessPointMode.PublicInToken && x.IpAddress.AddressFamily == addressFamily))
            return; // already set

        var firstPublic = accessPoints.FirstOrDefault(x =>
            x.AccessPointMode == AccessPointMode.Public &&
            x.IpAddress.AddressFamily == addressFamily);

        if (firstPublic == null)
            return; // not public found to select as PublicInToken

        accessPoints.Remove(firstPublic);
        accessPoints.Add(new AccessPointModel
        {
            AccessPointMode = AccessPointMode.PublicInToken,
            IsListen = firstPublic.IsListen,
            IpAddress = firstPublic.IpAddress,
            TcpPort = firstPublic.TcpPort,
            UdpPort = firstPublic.UdpPort
        });
    }

    private static void SetServerStatus(ServerModel server, ServerStatus serverStatus, bool isConfigure)
    {
        var serverStatusEx = new ServerStatusModel
        {
            ProjectId = server.ProjectId,
            ServerId = server.ServerId,
            IsConfigure = isConfigure,
            IsLast = true,
            CreatedTime = DateTime.UtcNow,
            AvailableMemory = serverStatus.AvailableMemory,
            CpuUsage = (byte?)serverStatus.CpuUsage,
            TcpConnectionCount = serverStatus.TcpConnectionCount,
            UdpConnectionCount = serverStatus.UdpConnectionCount,
            SessionCount = serverStatus.SessionCount,
            ThreadCount = serverStatus.ThreadCount,
            TunnelSendSpeed = serverStatus.TunnelSpeed.Sent,
            TunnelReceiveSpeed = serverStatus.TunnelSpeed.Received,
        };
        server.ServerStatus = serverStatusEx;
    }
}
