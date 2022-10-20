using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Renci.SshNet;
using VpnHood.AccessServer.Clients;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Exceptions;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.MultiLevelAuthorization.Repos;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Security;
using VpnHood.AccessServer.ServerUtils;
using VpnHood.Common;
using VpnHood.Server.AccessServers;

namespace VpnHood.AccessServer.Controllers;

[Route("/api/projects/{projectId:guid}/servers")]
public class ServerController : SuperController<ServerController>
{
    private readonly VhReportContext _vhReportContext;
    private readonly AppOptions _appOptions;
    private readonly AgentCacheClient _agentCacheClient;
    private readonly AgentSystemClient _agentSystemClient;

    public ServerController(
        ILogger<ServerController> logger,
        VhContext vhContext,
        VhReportContext vhReportContext,
        IOptions<AppOptions> appOptions,
        MultilevelAuthRepo multilevelAuthRepo,
        AgentCacheClient agentCacheClient, 
        AgentSystemClient agentSystemClient)
        : base(logger, vhContext, multilevelAuthRepo)
    {
        _vhReportContext = vhReportContext;
        _agentCacheClient = agentCacheClient;
        _agentSystemClient = agentSystemClient;
        _appOptions = appOptions.Value;
    }

    [HttpPost]
    public async Task<Models.Server> Create(Guid projectId, ServerCreateParams? createParams)
    {
        await VerifyUserPermission(VhContext, projectId, Permissions.ServerWrite);
        createParams ??= new ServerCreateParams();

        // check user quota
        using var singleRequest = SingleRequest.Start($"CreateServer_{CurrentUserId}");
        if (VhContext.Servers.Count(x => x.ProjectId == projectId) >= QuotaConstants.ServerCount)
            throw new QuotaException(nameof(VhContext.Servers), QuotaConstants.ServerCount);

        // validate
        var accessPointGroup = createParams.AccessPointGroupId != null
            ? await VhContext.AccessPointGroups.SingleAsync(x => x.ProjectId == projectId && x.AccessPointGroupId == createParams.AccessPointGroupId)
            : null;

        // Resolve Name Template
        createParams.ServerName = createParams.ServerName?.Trim();
        if (string.IsNullOrWhiteSpace(createParams.ServerName)) createParams.ServerName = Resource.NewServerTemplate;
        if (createParams.ServerName.Contains("##"))
        {
            var names = await VhContext.Servers
                .Where(x => x.ProjectId == projectId)
                .Select(x => x.ServerName)
                .ToArrayAsync();
            createParams.ServerName = AccessUtil.FindUniqueName(createParams.ServerName, names);
        }

        var server = new Models.Server
        {
            ProjectId = projectId,
            ServerId = Guid.NewGuid(),
            CreatedTime = DateTime.UtcNow,
            ServerName = createParams.ServerName,
            IsEnabled = true,
            Secret = Util.GenerateSessionKey(),
            AuthorizationCode = Guid.NewGuid(),
            AccessPointGroupId = accessPointGroup?.AccessPointGroupId,
            AccessPoints = new List<AccessPoint>(),
            ConfigCode = Guid.NewGuid()
        };
        await VhContext.Servers.AddAsync(server);
        await VhContext.SaveChangesAsync();

        return server;
    }

    [HttpPost("{serverId:guid}/reconfigure")]
    public async Task Reconfigure(Guid projectId, Guid serverId)
    {
        await VerifyUserPermission(VhContext, projectId, Permissions.ServerWrite);

        // validate
        var server = await VhContext.Servers
            .SingleAsync(x => x.ProjectId == projectId && x.ServerId == serverId);

        server.ConfigCode = Guid.NewGuid();
        await VhContext.SaveChangesAsync();
        await _agentCacheClient.InvalidateServer(server.ServerId);
    }

    [HttpPatch("{serverId:guid}")]
    public async Task<Models.Server> Update(Guid projectId, Guid serverId, ServerUpdateParams updateParams)
    {
        await VerifyUserPermission(VhContext, projectId, Permissions.ServerWrite);

        // validate
        var server = await VhContext.Servers
            .Include(x => x.AccessPoints)
            .SingleAsync(x => x.ProjectId == projectId && x.ServerId == serverId);

        if (updateParams.AccessPointGroupId != null && server.AccessPointGroupId != updateParams.AccessPointGroupId)
        {
            // make sure new access group belong to this server
            var accessPointGroup = updateParams.AccessPointGroupId.Value != null
                ? await VhContext.AccessPointGroups.SingleAsync(x => x.ProjectId == projectId && x.AccessPointGroupId == updateParams.AccessPointGroupId)
                : null;

            // update server accessPointGroup and all AccessPoints accessPointGroup
            server.AccessPointGroup = accessPointGroup;
            server.AccessPointGroupId = accessPointGroup?.AccessPointGroupId;
            if (accessPointGroup != null)
            {
                foreach (var accessPoint in server.AccessPoints!)
                {
                    accessPoint.AccessPointGroup = accessPointGroup;
                    accessPoint.AccessPointGroupId = accessPointGroup.AccessPointGroupId;
                }
            }

            // Schedule server reconfig
            server.ConfigCode = Guid.NewGuid();
        }

        if (updateParams.ServerName != null) server.ServerName = updateParams.ServerName;
        if (updateParams.GenerateNewSecret?.Value == true) server.Secret = Util.GenerateSessionKey();

        await VhContext.SaveChangesAsync();
        await _agentCacheClient.InvalidateServer(server.ServerId);

        return server;
    }

    [HttpGet("{serverId:guid}/status-logs")]
    public async Task<ServerStatusEx[]> GetStatusLogs(Guid projectId, Guid serverId, int recordIndex = 0,
        int recordCount = 1000)
    {
        await VerifyUserPermission(VhContext, projectId, Permissions.ProjectRead);

        //no lock
        await using var transReport = await _vhReportContext.WithNoLockTransaction();
        var list = await _vhReportContext.ServerStatuses.AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.ServerId == serverId)
            .OrderByDescending(x => x.ServerStatusId)
            .Skip(recordIndex)
            .Take(recordCount)
            .ToArrayAsync();

        return list;
    }

    [HttpGet("{serverId:guid}")]
    public async Task<ServerData> Get(Guid projectId, Guid serverId)
    {
        await VerifyUserPermission(VhContext, projectId, Permissions.ProjectRead);
        
        var server = await VhContext.Servers
            .Include(x=>x.AccessPointGroup)
            .Include(x=>x.AccessPoints!)
            .ThenInclude(accessPoint => accessPoint.AccessPointGroup)
            .SingleAsync(x=>x.ServerId==serverId);

        var cachedServers = await _agentCacheClient.GetServers(projectId);
        foreach (var cachedServer in cachedServers.Where(x => x.ServerId == serverId))
            server.ServerStatus = cachedServer.ServerStatus;

        var serverData = new ServerData
        {
            Server = server,
            AccessPoints = server.AccessPoints,
            Status = server.ServerStatus,
            State = ServerUtil.GetServerState(server, _appOptions.LostServerThreshold) //todo get from cache
        };

        return serverData;
    }

    [HttpGet]
    public async Task<ServerData[]> List(Guid projectId, Guid? serverId = null, int recordIndex = 0, int recordCount = 1000)
    {
        await VerifyUserPermission(VhContext, projectId, Permissions.ProjectRead);

        // no lock
        await using var trans = await VhContext.WithNoLockTransaction();

        var query =
            from server in VhContext.Servers
            join serverStatusLog in VhContext.ServerStatuses on new { key1 = server.ServerId, key2 = true } equals
                new { key1 = serverStatusLog.ServerId, key2 = serverStatusLog.IsLast } into grouping
            from serverStatus in grouping.DefaultIfEmpty()
            join accessPoint in VhContext.AccessPoints on server.ServerId equals accessPoint.ServerId into grouping2
            from accessPoint in grouping2.DefaultIfEmpty()
            join accessPointGroup in VhContext.AccessPointGroups on accessPoint.AccessPointGroupId equals accessPointGroup.AccessPointGroupId into grouping3
            from accessPointGroup in grouping3.DefaultIfEmpty()
            join accessPointGroup2 in VhContext.AccessPointGroups on server.AccessPointGroupId equals accessPointGroup2.AccessPointGroupId into grouping4
            from accessPointGroup2 in grouping4.DefaultIfEmpty()

            where server.ProjectId == projectId
            select new { server, serverStatus, accessPointGroup, accessPointGroup2, accessPoint };

        if (serverId != null)
            query = query.Where(x => x.server.ServerId == serverId);

        var res = await query
            .OrderBy(x => x.server.ServerId)
            .Skip(recordIndex)
            .Take(recordCount)
            .ToArrayAsync();

        var ret =
            res.GroupBy(x => x.server.ServerId)
                .Select(x => x.First())
                .Select(x => new ServerData
                {
                    Server = x.server,
                    AccessPoints = x.server.AccessPoints ?? Array.Empty<AccessPoint>(),
                    Status = x.serverStatus
                })
                .ToArray();

        // update from cache
        var cachedServers = await _agentCacheClient.GetServers(projectId);
        foreach (var serverData in ret)
        {
            serverData.Status = cachedServers.FirstOrDefault(x => x.ServerId == serverData.Server.ServerId)?.ServerStatus ?? serverData.Status;
            serverData.State = ServerUtil.GetServerState(serverData.Server, _appOptions.LostServerThreshold);
        }

        return ret;
    }

    private static async Task<string> ExecuteSshCommand(SshClient sshClient, string command, string? password, TimeSpan timeout)
    {
        command += ";echo 'CommandExecuted''!'";
        if (!string.IsNullOrEmpty(password)) command += $"\r{password}\r";
        await using var shellStream = sshClient.CreateShellStream("ShellStreamCommand", 0, 0, 0, 0, 2048);
        shellStream.WriteLine(command);
        await shellStream.FlushAsync();
        var res = shellStream.Expect("CommandExecuted!", timeout);
        return res;
    }

    [HttpPost("{serverId:guid}/install-by-ssh-user-password")]
    [Produces(MediaTypeNames.Text.Plain)]
    public async Task InstallBySshUserPassword(Guid projectId, Guid serverId, ServerInstallBySshUserPasswordParams installParams)
    {
        await VerifyUserPermission(VhContext, projectId, Permissions.ServerInstall);

        var hostPort = installParams.HostPort == 0 ? 22 : installParams.HostPort;
        var connectionInfo = new ConnectionInfo(installParams.HostName, hostPort, installParams.UserName, new PasswordAuthenticationMethod(installParams.UserName, installParams.Password));

        var appSettings = await GetInstallAppSettings(VhContext, projectId, serverId);
        await InstallBySsh(appSettings, connectionInfo, installParams.Password);
    }

    [HttpPost("{serverId:guid}/install-by-ssh-user-key")]
    public async Task InstallBySshUserKey(Guid projectId, Guid serverId, ServerInstallBySshUserKeyParams installParams)
    {
        await VerifyUserPermission(VhContext, projectId, Permissions.ServerInstall);

        await using var keyStream = new MemoryStream(installParams.UserKey);
        using var privateKey = new PrivateKeyFile(keyStream, installParams.UserKeyPassphrase);
        var connectionInfo = new ConnectionInfo(installParams.HostName, installParams.HostPort, installParams.UserName, new PrivateKeyAuthenticationMethod(installParams.UserName, privateKey));

        var appSettings = await GetInstallAppSettings(VhContext, projectId, serverId);
        await InstallBySsh(appSettings, connectionInfo, null);
    }

    private async Task InstallBySsh(ServerInstallAppSettings appSettings, ConnectionInfo connectionInfo, string? userPassword)
    {
        using var sshClient = new SshClient(connectionInfo);
        sshClient.Connect();

        var linuxCommand = GetInstallLinuxCommand(appSettings, false);

        var res = await ExecuteSshCommand(sshClient, linuxCommand, userPassword, TimeSpan.FromMinutes(5));
        res = res.Replace($"\n{userPassword}", "\n********");

        var check = sshClient.RunCommand("dir /opt/VpnHoodServer");
        var checkResult = check.Execute();
        if (checkResult.IndexOf("publish.json", StringComparison.Ordinal) == -1)
        {
            var ex = new Exception("Installation failed! Check detail for more information.");
            ex.Data.Add("log", res);
            throw ex;
        }
    }

    [HttpGet("{serverId:guid}/install-by-manual")]
    public async Task<ServerInstallManual> InstallByManual(Guid projectId, Guid serverId)
    {
        await VerifyUserPermission(VhContext, projectId, Permissions.ServerReadConfig);

        var appSettings = await GetInstallAppSettings(VhContext, projectId, serverId);
        var ret = new ServerInstallManual(appSettings, GetInstallLinuxCommand(appSettings, true));
        return ret;
    }

    private async Task<ServerInstallAppSettings> GetInstallAppSettings(VhContext vhContext, Guid projectId, Guid serverId)
    {
        // make sure server belongs to project
        var server = await vhContext.Servers.SingleAsync(x => x.ProjectId == projectId && x.ServerId == serverId);

        // create jwt
        var authorization = await _agentSystemClient.GetAgentAuthorization(server.ServerId);
        var agentUri = new Uri(_appOptions.AgentUri, "/api/agent/");
        var url = agentUri.AbsoluteUri ?? throw new Exception("AgentUri is not set!");
        var appSettings = new ServerInstallAppSettings(new RestAccessServerOptions(url, authorization), server.Secret);
        return appSettings;
    }

    private string GetInstallLinuxCommand(ServerInstallAppSettings installAppSettings, bool manual)
    {
        var autoCommand = manual ? "" : "-q -autostart ";

        var linuxCommand =
            "sudo su -c \"bash <( wget -qO- https://github.com/vpnhood/VpnHood/releases/latest/download/install-linux.sh) " +
            autoCommand +
            $"-secret '{Convert.ToBase64String(installAppSettings.Secret)}' " +
            $"-restBaseUrl '{installAppSettings.RestAccessServer.BaseUrl}' " +
            $"-restAuthorization '{installAppSettings.RestAccessServer.Authorization}'\"";

        return linuxCommand;
    }
}