using System.Net;
using System.Net.Sockets;
using VpnHood.AccessServer.Persistence;
using Microsoft.EntityFrameworkCore;
using VpnHood.AccessServer.Clients;
using Microsoft.Extensions.Options;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos;
using VpnHood.Common.Utils;
using VpnHood.AccessServer.Exceptions;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Utils;
using VpnHood.Server.Access.Managers.Http;
using Renci.SshNet;

namespace VpnHood.AccessServer.Services;

public class ServerService(
    VhRepo vhRepo,
    VhContext vhContext,
    IOptions<AppOptions> appOptions,
    AgentCacheClient agentCacheClient,
    SubscriptionService subscriptionService,
    AgentSystemClient agentSystemClient)
{
    public async Task<VpnServer> Create(Guid projectId, ServerCreateParams createParams)
    {
        // check user quota
        using var singleRequest = await AsyncLock.LockAsync($"CreateServer_{projectId}");
        await subscriptionService.AuthorizeCreateServer(projectId);

        // validate
        var serverFarm = await vhRepo.GetServerFarm(projectId, createParams.ServerFarmId, true, true);

        // Resolve Name Template
        var serverName = createParams.ServerName?.Trim();
        if (string.IsNullOrWhiteSpace(serverName)) serverName = Resource.NewServerTemplate;
        if (serverName.Contains("##"))
        {
            var names = await vhRepo.GetServerNames(projectId);
            serverName = AccessUtil.FindUniqueName(serverName, names);
        }

        var server = new ServerModel
        {
            ProjectId = projectId,
            ServerId = Guid.NewGuid(),
            CreatedTime = DateTime.UtcNow,
            ServerName = serverName,
            IsEnabled = true,
            ManagementSecret = VhUtil.GenerateKey(),
            AuthorizationCode = Guid.NewGuid(),
            ServerFarmId = serverFarm.ServerFarmId,
            AccessPoints = ValidateAccessPoints(createParams.AccessPoints ?? Array.Empty<AccessPoint>()),
            ConfigCode = Guid.NewGuid(),
            AutoConfigure = createParams.AccessPoints == null,
        };

        // add server and update FarmToken
        serverFarm.Servers!.Add(server);
        FarmTokenBuilder.UpdateIfChanged(serverFarm);

        await vhRepo.AddAsync(server);
        await vhRepo.SaveChangesAsync();


        var serverDto = server.ToDto(appOptions.Value.LostServerThreshold);
        return serverDto;

    }

    public async Task<VpnServer> Update(Guid projectId, Guid serverId, ServerUpdateParams updateParams)
    {
        if (updateParams.AutoConfigure?.Value == true && updateParams.AccessPoints != null)
            throw new ArgumentException($"{nameof(updateParams.AutoConfigure)} can not be true when {nameof(updateParams.AccessPoints)} is set", nameof(updateParams));

        // validate
        var server = await vhRepo.GetServer(projectId, serverId);
        var oldConfigCode = server.ConfigCode;

        if (updateParams.ServerFarmId != null)
        {
            // make sure new farm belong to this account
            var serverFarm = await vhRepo.GetServerFarm(projectId, updateParams.ServerFarmId);
            server.ServerFarmId = serverFarm.ServerFarmId;
        }
        if (updateParams.GenerateNewSecret?.Value == true) server.ManagementSecret = VhUtil.GenerateKey();
        if (updateParams.ServerName != null) server.ServerName = updateParams.ServerName;
        if (updateParams.AutoConfigure != null) server.AutoConfigure = updateParams.AutoConfigure;
        if (updateParams.AccessPoints != null)
        {
            server.AutoConfigure = false;
            server.AccessPoints = ValidateAccessPoints(updateParams.AccessPoints);
        }

        // reconfig if required
        if (updateParams.AccessPoints != null || updateParams.AutoConfigure != null || updateParams.AccessPoints != null || updateParams.ServerFarmId != null)
            server.ConfigCode = Guid.NewGuid();

        await vhRepo.SaveChangesAsync();

        // update FarmToken if config has been changed
        if (oldConfigCode != server.ConfigCode)
        {
            var serverFarm = await vhRepo.GetServerFarm(projectId, server.ServerFarmId, true, true);
            FarmTokenBuilder.UpdateIfChanged(serverFarm);
            await vhRepo.SaveChangesAsync();
        }

        await agentCacheClient.InvalidateServer(server.ServerId);
        var serverCache = await agentCacheClient.GetServer(server.ServerId);
        return serverCache ?? server.ToDto(appOptions.Value.LostServerThreshold);
    }

    public async Task<ServerData[]> List(Guid projectId,
        string? search = null,
        Guid? serverId = null,
        Guid? serverFarmId = null,
        int recordIndex = 0,
        int recordCount = int.MaxValue)
    {
        // no lock
        await using var trans = await vhContext.WithNoLockTransaction();

        var query = vhContext.Servers
            .Where(server => server.ProjectId == projectId && !server.IsDeleted)
            .Include(server => server.ServerFarm)
            .Where(server => serverId == null || server.ServerId == serverId)
            .Where(server => serverFarmId == null || server.ServerFarmId == serverFarmId)
            .Where(x =>
                string.IsNullOrEmpty(search) ||
                x.ServerName.Contains(search) ||
                x.ServerId.ToString() == search ||
                x.ServerFarmId.ToString() == search);

        var servers = await query
            .AsNoTracking()
            .OrderBy(x => x.ServerId)
            .Skip(recordIndex)
            .Take(recordCount)
            .ToArrayAsync();

        // update all status
        var serverStatus = await vhContext.ServerStatuses
            .AsNoTracking()
            .Where(serverStatus =>
                serverStatus.IsLast && serverStatus.ProjectId == projectId &&
                (serverId == null || serverStatus.ServerId == serverId) &&
                (serverFarmId == null || serverStatus.Server!.ServerFarmId == serverFarmId))
            .ToDictionaryAsync(x => x.ServerId);

        foreach (var serverModel in servers)
            if (serverStatus.TryGetValue(serverModel.ServerId, out var serverStatusEx))
                serverModel.ServerStatus = serverStatusEx;

        // create Dto
        var serverDatas = servers
            .Select(serverModel => new ServerData
            {
                Server = serverModel.ToDto(appOptions.Value.LostServerThreshold)
            })
            .ToArray();

        // update from cache
        var cachedServers = await agentCacheClient.GetServers(projectId);
        foreach (var serverData in serverDatas)
        {
            var cachedServer = cachedServers.SingleOrDefault(x => x.ServerId == serverData.Server.ServerId);
            if (cachedServer == null) continue;
            serverData.Server.ServerStatus = cachedServer.ServerStatus;
            serverData.Server.ServerState = cachedServer.ServerState;
        }

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
        var servers = serverModels
            .Select(server => server.ToDto(appOptions.Value.LostServerThreshold))
            .ToArray();

        // update status from cache
        var cachedServers = await agentCacheClient.GetServers(projectId);
        ServerUtil.UpdateByCache(servers, cachedServers);

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
            TunnelReceiveSpeed = servers.Where(x => x.ServerState == ServerState.Active).Sum(x => x.ServerStatus!.TunnelReceiveSpeed),
        };

        return usageSummary;
    }

    public async Task ReconfigServers(Guid projectId, Guid? serverFarmId = null,
        Guid? serverProfileId = null, Guid? certificateId = null)
    {
        var servers = await vhContext.Servers.Where(server =>
            server.ProjectId == projectId &&
            (serverFarmId == null || server.ServerFarmId == serverFarmId) &&
            (serverProfileId == null || server.ServerFarm!.ServerProfileId == serverProfileId) &&
            (certificateId == null || server.ServerFarm!.CertificateId == certificateId))
            .ToArrayAsync();

        foreach (var server in servers)
            server.ConfigCode = Guid.NewGuid();

        await vhContext.SaveChangesAsync();
        await agentCacheClient.InvalidateProjectServers(projectId,
            serverFarmId: serverFarmId,
            serverProfileId: serverProfileId,
            certificateId: certificateId);
    }

    public async Task Delete(Guid projectId, Guid serverId)
    {
        var server = await vhContext.Servers
            .Where(server => server.ProjectId == projectId && !server.IsDeleted)
            .SingleAsync(server => server.ServerId == serverId);

        server.IsDeleted = true;
        await vhContext.SaveChangesAsync();
    }

    public async Task Reconfigure(Guid projectId, Guid serverId)
    {
        var server = await vhContext.Servers
            .Where(x => x.ProjectId == projectId && !x.IsDeleted)
            .SingleAsync(x => x.ServerId == serverId);

        server.ConfigCode = Guid.NewGuid();
        await vhContext.SaveChangesAsync();
        await agentCacheClient.InvalidateServer(server.ServerId);
    }

    public async Task InstallBySshUserPassword(Guid projectId, Guid serverId, ServerInstallBySshUserPasswordParams installParams)
    {

        var hostPort = installParams.HostPort == 0 ? 22 : installParams.HostPort;
        var connectionInfo = new Renci.SshNet.ConnectionInfo(installParams.HostName, hostPort, installParams.LoginUserName, new PasswordAuthenticationMethod(installParams.LoginUserName, installParams.LoginPassword));

        var appSettings = await GetInstallAppSettings(projectId, serverId);
        await InstallBySsh(appSettings, connectionInfo, installParams.LoginPassword);
    }

    public async Task InstallBySshUserKey(Guid projectId, Guid serverId, ServerInstallBySshUserKeyParams installParams)
    {
        await using var keyStream = new MemoryStream(installParams.UserPrivateKey);
        using var privateKey = new PrivateKeyFile(keyStream, installParams.UserPrivateKeyPassphrase);

        var connectionInfo = new Renci.SshNet.ConnectionInfo(installParams.HostName, installParams.HostPort, installParams.LoginUserName, new PrivateKeyAuthenticationMethod(installParams.LoginUserName, privateKey));

        var appSettings = await GetInstallAppSettings(projectId, serverId);
        await InstallBySsh(appSettings, connectionInfo, installParams.LoginPassword);
    }

    private static async Task InstallBySsh(ServerInstallAppSettings appSettings, Renci.SshNet.ConnectionInfo connectionInfo, string? loginPassword)
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

}
