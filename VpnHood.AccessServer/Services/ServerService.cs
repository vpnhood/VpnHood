using System.Net;
using System.Net.Sockets;
using GrayMint.Common.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Renci.SshNet;
using VpnHood.AccessServer.Clients;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Dtos.Servers;
using VpnHood.AccessServer.Exceptions;
using VpnHood.AccessServer.Options;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Persistence.Caches;
using VpnHood.AccessServer.Persistence.Enums;
using VpnHood.AccessServer.Persistence.Models;
using VpnHood.AccessServer.Persistence.Utils;
using VpnHood.AccessServer.Repos;
using VpnHood.Server.Access.Managers.Http;
using ConnectionInfo = Renci.SshNet.ConnectionInfo;

namespace VpnHood.AccessServer.Services;

public class ServerService(
    VhRepo vhRepo,
    VhContext vhContext,
    IOptions<AppOptions> appOptions,
    AgentCacheClient agentCacheClient,
    ServerConfigureService serverConfigureService,
    SubscriptionService subscriptionService,
    AgentSystemClient agentSystemClient)
{
    public async Task<ServerData> Create(Guid projectId, ServerCreateParams createParams)
    {
        // check user quota
        using var singleRequest = await AsyncLock.LockAsync($"CreateServer_{projectId}");
        await subscriptionService.AuthorizeCreateServer(projectId);

        // make sure farm belongs to this account
        var serverFarm = await vhRepo.ServerFarmGet(projectId, createParams.ServerFarmId, includeServers: true);

        // Resolve Name Template
        var serverName = createParams.ServerName?.Trim();
        if (string.IsNullOrWhiteSpace(serverName)) serverName = Resource.NewServerTemplate;
        if (serverName.Contains("##"))
        {
            var names = await vhRepo.ServerGetNames(projectId);
            serverName = AccessServerUtil.FindUniqueName(serverName, names);
        }

        var server = new ServerModel
        {
            ProjectId = projectId,
            ServerId = Guid.NewGuid(),
            CreatedTime = DateTime.UtcNow,
            ServerName = serverName,
            IsEnabled = true,
            ManagementSecret = GmUtil.GenerateKey(),
            AuthorizationCode = Guid.NewGuid(),
            ServerFarmId = serverFarm.ServerFarmId,
            AccessPoints = ValidateAccessPoints(createParams.AccessPoints ?? []),
            ConfigCode = Guid.NewGuid(),
            AutoConfigure = createParams.AccessPoints == null,
            Description = null,
            LastConfigCode = null,
            LastConfigError = null,
            LogicalCoreCount = null,
            OsInfo = null,
            TotalMemory = null,
            Version = null,
            ConfigureTime = null,
            EnvironmentVersion = null,
            MachineName = null,
            GatewayIpV4 = null,
            GatewayIpV6 = null,
            LocationId = null,
            IsDeleted = false
        };

        // add server and update FarmToken
        serverFarm.Servers!.Add(server);
        await vhRepo.AddAsync(server); // to help server farm to update token
        await serverConfigureService.SaveChangesAndInvalidateServerFarm(projectId, server.ServerFarmId, false);
        var serverData = new ServerData
        {
            Server = server.ToDto(null)
        };
        return serverData;
    }

    public async Task<ServerData> Update(Guid projectId, Guid serverId, ServerUpdateParams updateParams)
    {
        if (updateParams.AutoConfigure?.Value == true && updateParams.AccessPoints != null)
            throw new ArgumentException($"{nameof(updateParams.AutoConfigure)} can not be true when {nameof(updateParams.AccessPoints)} is set", nameof(updateParams));

        // validate
        var server = await vhRepo.ServerGet(projectId, serverId, includeFarm: true);
        var oldServerFarmId = server.ServerFarmId;

        if (updateParams.ServerFarmId != null)
        {
            // make sure new farm belong to this account and ready for update farm token
            var serverFarm = await vhRepo.ServerFarmGet(projectId, updateParams.ServerFarmId);
            server.ServerFarmId = serverFarm.ServerFarmId;
        }
        if (updateParams.GenerateNewSecret?.Value == true) server.ManagementSecret = GmUtil.GenerateKey();
        if (updateParams.ServerName != null) server.ServerName = updateParams.ServerName;
        if (updateParams.AutoConfigure != null) server.AutoConfigure = updateParams.AutoConfigure;
        if (updateParams.AccessPoints != null)
        {
            server.AutoConfigure = false;
            server.AccessPoints = ValidateAccessPoints(updateParams.AccessPoints);
        }

        // update farm token
        await serverConfigureService.SaveChangesAndInvalidateServerFarm(projectId, server.ServerFarmId, false);
        if (oldServerFarmId != server.ServerFarmId)
            await serverConfigureService.SaveChangesAndInvalidateServerFarm(projectId, oldServerFarmId, false);

        // reconfig current server if required
        var reconfigure = updateParams.AccessPoints != null || updateParams.AutoConfigure != null || updateParams.ServerFarmId != null;
        var serverCache = await serverConfigureService.SaveChangesAndInvalidateServer(projectId, serverId, reconfigure);

        // get server again to resolve region and farm
        server = await vhRepo.ServerGet(projectId, serverId: serverId, includeFarm: true);
        var serverData = new ServerData
        {
            Server = server.ToDto(serverCache)
        };
        return serverData;
    }

    public async Task<ServerData> Get(Guid projectId, Guid serverId)
    {
        var items = await List(projectId, serverId: serverId);
        return items.Single();
    }

    public async Task<ServerData[]> List(
        Guid projectId,
        string? search = null,
        Guid? serverId = null,
        Guid? serverFarmId = null,
        int recordIndex = 0,
        int recordCount = int.MaxValue)
    {
        var servers = await vhRepo.ServerSearch(projectId: projectId,
            search: search, serverId: serverId, serverFarmId: serverFarmId,
            recordIndex: recordIndex, recordCount: recordCount);

        // create Dto
        var cachedServers = await agentCacheClient.GetServers(projectId);
        var serverDatas = servers
            .Select(serverModel => new ServerData
            {
                Server = serverModel.ToDto(cachedServers.FirstOrDefault(x => x.ServerId == serverModel.ServerId))
            })
            .ToArray();

        // update server status if it is lost
        foreach (var serverData in serverDatas.Where(x => x.Server.ServerState is ServerState.Lost or ServerState.NotInstalled))
            serverData.Server.ServerStatus = null;

        return serverDatas;
    }

    private static List<AccessPointModel> ValidateAccessPoints(AccessPoint[] accessPoints)
    {
        if (accessPoints.Length > QuotaConstants.AccessPointCount)
            throw new QuotaException(nameof(QuotaConstants.AccessPointCount), QuotaConstants.AccessPointCount);

        // validate public ips
        var anyIpAddress4Public = accessPoints.SingleOrDefault(x =>
            x.AccessPointMode is AccessPointMode.Public or AccessPointMode.PublicInToken &&
            (x.IpAddress.Equals(IPAddress.Any) || x.IpAddress.Equals(IPAddress.IPv6Any)))?.IpAddress;
        if (anyIpAddress4Public != null) throw new InvalidOperationException($"Can not use {anyIpAddress4Public} as public a address.");

        // validate TcpEndPoints
        _ = accessPoints.Select(x => new IPEndPoint(x.IpAddress, x.TcpPort));
        if (accessPoints.Any(x => x.TcpPort == 0)) throw new InvalidOperationException("Invalid TcpEndPoint. Port can not be zero.");

        //find duplicate tcp
        var duplicate = accessPoints
            .GroupBy(x => $"{x.IpAddress}:{x.TcpPort}-{x.IsListen}")
            .Where(g => g.Count() > 1)
            .Select(g => g.First())
            .FirstOrDefault();

        if (duplicate != null)
            throw new InvalidOperationException($"Duplicate TCP listener on a single IP is not possible. {duplicate.IpAddress}:{duplicate.TcpPort}");

        //find duplicate tcp on any ipv4
        var anyPorts = accessPoints.Where(x => x.IsListen && x.IpAddress.Equals(IPAddress.Any)).Select(x => x.TcpPort);
        duplicate = accessPoints.FirstOrDefault(x =>
            x is { IsListen: true, IpAddress.AddressFamily: AddressFamily.InterNetwork } &&
            !x.IpAddress.Equals(IPAddress.Any) && anyPorts.Contains(x.TcpPort));
        if (duplicate != null)
            throw new InvalidOperationException($"Duplicate TCP listener on a single IP is not possible. {duplicate.IpAddress}:{duplicate.TcpPort}");

        //find duplicate tcp on any ipv6
        anyPorts = accessPoints.Where(x => x.IsListen && x.IpAddress.Equals(IPAddress.IPv6Any)).Select(x => x.TcpPort);
        duplicate = accessPoints.FirstOrDefault(x =>
            x is { IsListen: true, IpAddress.AddressFamily: AddressFamily.InterNetworkV6 } &&
            !x.IpAddress.Equals(IPAddress.IPv6Any) && anyPorts.Contains(x.TcpPort));
        if (duplicate != null)
            throw new InvalidOperationException($"Duplicate TCP listener on a single IP is not possible. {duplicate.IpAddress}:{duplicate.TcpPort}");

        // validate UdpEndPoints
        _ = accessPoints.Where(x => x.UdpPort != -1).Select(x => new IPEndPoint(x.IpAddress, x.UdpPort));
        if (accessPoints.Any(x => x.UdpPort == 0)) throw new InvalidOperationException("Invalid UdpEndPoint. Port can not be zero.");

        //find duplicate udp
        duplicate = accessPoints
            .Where(x => x.UdpPort > 0)
            .GroupBy(x => $"{x.IpAddress}:{x.UdpPort}-{x.IsListen}")
            .Where(g => g.Count() > 1)
            .Select(g => g.First())
            .FirstOrDefault();

        if (duplicate != null)
            throw new InvalidOperationException($"Duplicate UDP listener on a single IP is not possible. {duplicate.IpAddress}:{duplicate.UdpPort}");

        //find duplicate udp on any ipv4
        anyPorts = accessPoints.Where(x =>
            x is { IsListen: true, UdpPort: > 0 } &&
            x.IpAddress.Equals(IPAddress.Any)).Select(x => x.UdpPort);
        duplicate = accessPoints.FirstOrDefault(x =>
            x is { IsListen: true, UdpPort: > 0, IpAddress.AddressFamily: AddressFamily.InterNetwork } &&
            !x.IpAddress.Equals(IPAddress.Any) && anyPorts.Contains(x.UdpPort));
        if (duplicate != null)
            throw new InvalidOperationException($"Duplicate UDP listener on a single IP is not possible. {duplicate.IpAddress}:{duplicate.UdpPort}");

        //find duplicate udp on any ipv6
        anyPorts = accessPoints.Where(x =>
            x is { IsListen: true, UdpPort: > 0 } && x.IpAddress.Equals(IPAddress.IPv6Any)).Select(x => x.UdpPort);
        duplicate = accessPoints.FirstOrDefault(x =>
            x is { IsListen: true, UdpPort: > 0, IpAddress.AddressFamily: AddressFamily.InterNetworkV6 } &&
            !x.IpAddress.Equals(IPAddress.IPv6Any) && anyPorts.Contains(x.UdpPort));
        if (duplicate != null)
            throw new InvalidOperationException($"Duplicate UDP listener on a single IP is not possible. {duplicate.IpAddress}:{duplicate.UdpPort}");

        return accessPoints.Select(x => x.ToModel()).ToList();
    }

    public async Task<ServersStatusSummary> GetStatusSummary(Guid projectId, Guid? serverFarmId = null)
    {
        // no lock
        await using var trans = await vhContext.WithNoLockTransaction();

        /*
        var query = VhContext.Servers
            .Where(server => server.ProjectId == projectId && !server.IsDeleted)
            .Where(server => serverFarmId == null || server.ServerFarmId == serverFarmId)
            .GroupJoin(VhContext.ServerStatuses,
                server => new { key1 = server.ServerId, key2 = true },
                serverStatus => new { key1 = serverStatus.ServerId, key2 = serverStatus.IsLast },
                (server, serverStatus) => new { server, serverStatus })
            .SelectMany(
                joinResult => joinResult.serverStatus.DefaultIfEmpty(),
                (x, y) => new { Server = x.server, ServerStatus = y })
            .Select(s => new { s.Server, s.ServerStatus });

        // update model ServerStatusEx
        var serverModels = await query.ToArrayAsync();
        var servers = serverModels
            .Select(x => x.Server.ToDto(x.ServerStatus?.ToDto(), _appOptions.LostServerThreshold))
            .ToArray();
        */

        var serverModels = await vhContext.Servers
            .Where(server => server.ProjectId == projectId && !server.IsDeleted)
            .Where(server => serverFarmId == null || server.ServerFarmId == serverFarmId)
            .ToArrayAsync();

        // update model ServerStatusEx
        var cachedServers = await agentCacheClient.GetServers(projectId);
        var servers = serverModels
            .Select(server => server.ToDto(cachedServers.FirstOrDefault(x => x.ServerId == server.ServerId)))
            .ToArray();

        // create usage summary
        var usageSummary = new ServersStatusSummary
        {
            TotalServerCount = servers.Length,
            NotInstalledServerCount = servers.Count(x => x.ServerState is ServerState.NotInstalled),
            ActiveServerCount = servers.Count(x => x.ServerState is ServerState.Active),
            IdleServerCount = servers.Count(x => x.ServerState is ServerState.Idle),
            LostServerCount = servers.Count(x => x.ServerState is ServerState.Lost),
            SessionCount = servers.Where(x => x.ServerState is ServerState.Active).Sum(x => x.ServerStatus!.SessionCount),
            TunnelSendSpeed = servers.Where(x => x.ServerState is ServerState.Active).Sum(x => x.ServerStatus!.TunnelSendSpeed),
            TunnelReceiveSpeed = servers.Where(x => x.ServerState == ServerState.Active).Sum(x => x.ServerStatus!.TunnelReceiveSpeed)
        };

        return usageSummary;
    }

    public async Task Delete(Guid projectId, Guid serverId)
    {
        var server = await vhRepo.ServerGet(projectId, serverId);
        var serverFarm = await vhRepo.ServerFarmGet(projectId, server.ServerFarmId, includeServers: true, includeCertificate: true);
        server.IsDeleted = true;
        server.IsEnabled = true;

        var isFarmUpdated = FarmTokenBuilder.UpdateIfChanged(serverFarm);
        await vhContext.SaveChangesAsync();
        if (isFarmUpdated)
            await agentCacheClient.InvalidateServerFarm(server.ServerFarmId, includeSevers: false);

    }

    public async Task InstallBySshUserPassword(Guid projectId, Guid serverId, ServerInstallBySshUserPasswordParams installParams)
    {

        var hostPort = installParams.HostPort == 0 ? 22 : installParams.HostPort;
        var connectionInfo = new ConnectionInfo(installParams.HostName, hostPort, installParams.LoginUserName, new PasswordAuthenticationMethod(installParams.LoginUserName, installParams.LoginPassword));

        var appSettings = await GetInstallAppSettings(projectId, serverId);
        await InstallBySsh(appSettings, connectionInfo, installParams.LoginPassword);
    }

    public async Task InstallBySshUserKey(Guid projectId, Guid serverId, ServerInstallBySshUserKeyParams installParams)
    {
        await using var keyStream = new MemoryStream(installParams.UserPrivateKey);
        using var privateKey = new PrivateKeyFile(keyStream, installParams.UserPrivateKeyPassphrase);

        var connectionInfo = new ConnectionInfo(installParams.HostName, installParams.HostPort, installParams.LoginUserName, new PrivateKeyAuthenticationMethod(installParams.LoginUserName, privateKey));

        var appSettings = await GetInstallAppSettings(projectId, serverId);
        await InstallBySsh(appSettings, connectionInfo, installParams.LoginPassword);
    }

    private static async Task InstallBySsh(ServerInstallAppSettings appSettings, ConnectionInfo connectionInfo, string? loginPassword)
    {
        using var sshClient = new SshClient(connectionInfo);
        sshClient.Connect();

        var linuxCommand = GetInstallScriptForLinux(appSettings, false);
        var res = await AccessServerUtil.ExecuteSshCommand(sshClient, linuxCommand, loginPassword, TimeSpan.FromMinutes(5));

        var check = sshClient.RunCommand("dir /opt/VpnHoodServer");
        var checkResult = check.Execute();
        if (checkResult.IndexOf("publish.json", StringComparison.Ordinal) == -1)
        {
            var ex = new Exception("Installation failed! Check detail for more information.");
            ex.Data.Add("log", res);
            throw ex;
        }
    }

    public async Task<ServerInstallManual> GetInstallManual(Guid projectId, Guid serverId)
    {
        var appSettings = await GetInstallAppSettings(projectId, serverId);
        var ret = new ServerInstallManual(appSettings)
        {
            LinuxCommand = GetInstallScriptForLinux(appSettings, true),
            WindowsCommand = GetInstallScriptForWindows(appSettings, true)
        };

        return ret;
    }

    private async Task<ServerInstallAppSettings> GetInstallAppSettings(Guid projectId, Guid serverId)
    {
        // make sure server belongs to project
        var server = await vhContext.Servers
            .Where(server => server.ProjectId == projectId && !server.IsDeleted)
            .SingleAsync(server => server.ServerId == serverId);

        // create jwt
        var authorization = await agentSystemClient.GetServerAgentAuthorization(server.ServerId);
        var appSettings = new ServerInstallAppSettings
        {
            HttpAccessManager = new HttpAccessManagerOptions(appOptions.Value.AgentUrl, authorization),
            ManagementSecret = server.ManagementSecret
        };
        return appSettings;
    }

    private static string GetInstallScriptForLinux(ServerInstallAppSettings installAppSettings, bool manual)
    {
        var autoCommand = manual ? "" : "-q -autostart ";

        var script =
            "sudo su -c \"bash <( wget -qO- https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodServer-linux-x64.sh) " +
            autoCommand +
            $"-managementSecret '{Convert.ToBase64String(installAppSettings.ManagementSecret)}' " +
            $"-httpBaseUrl '{installAppSettings.HttpAccessManager.BaseUrl}' " +
            $"-httpAuthorization '{installAppSettings.HttpAccessManager.Authorization}'\"";

        return script;
    }

    private static string GetInstallScriptForWindows(ServerInstallAppSettings installAppSettings, bool manual)
    {
        var autoCommand = manual ? "" : "-q -autostart ";

        var script =
            "[Net.ServicePointManager]::SecurityProtocol = \"Tls,Tls11,Tls12\";" +
            "& ([ScriptBlock]::Create((Invoke-WebRequest(\"https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodServer-win-x64.ps1\")))) " +
            autoCommand +
            $"-managementSecret \"{Convert.ToBase64String(installAppSettings.ManagementSecret)}\" " +
            $"-httpBaseUrl \"{installAppSettings.HttpAccessManager.BaseUrl}\" " +
            $"-httpAuthorization \"{installAppSettings.HttpAccessManager.Authorization}\"";

        return script;
    }

    public Task<ServerCache?> Reconfigure(Guid projectId, Guid serverId)
    {
        return serverConfigureService.SaveChangesAndInvalidateServer(projectId: projectId, serverId: serverId, reconfigure: true);
    }

}
