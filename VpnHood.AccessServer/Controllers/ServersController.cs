using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VpnHood.AccessServer.Clients;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Dtos.ServerDtos;
using VpnHood.AccessServer.MultiLevelAuthorization.Services;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Security;
using VpnHood.AccessServer.ServerUtils;
using VpnHood.AccessServer.Services;
using VpnHood.Server.Providers.HttpAccessServerProvider;

namespace VpnHood.AccessServer.Controllers;

[Route("/api/v{version:apiVersion}/projects/{projectId:guid}/servers")]
public class ServersController : SuperController<ServersController>
{
    private readonly AppOptions _appOptions;
    private readonly AgentCacheClient _agentCacheClient;
    private readonly AgentSystemClient _agentSystemClient;
    private readonly UsageReportService _usageReportService;
    private readonly ServerService _serverService;

    public ServersController(
        ILogger<ServersController> logger,
        VhContext vhContext,
        IOptions<AppOptions> appOptions,
        MultilevelAuthService multilevelAuthService,
        AgentCacheClient agentCacheClient,
        AgentSystemClient agentSystemClient, UsageReportService usageReportService,
        ServerService serverService)
        : base(logger, vhContext, multilevelAuthService)
    {
        _agentCacheClient = agentCacheClient;
        _agentSystemClient = agentSystemClient;
        _usageReportService = usageReportService;
        _serverService = serverService;
        _appOptions = appOptions.Value;
    }

    [HttpPost]
    public async Task<Dtos.Server> Create(Guid projectId, ServerCreateParams createParams)
    {
        await VerifyUserPermission(projectId, Permissions.ServerWrite);
        return await _serverService.Create(projectId, createParams);
    }

    [HttpPatch("{serverId:guid}")]
    public async Task<Dtos.Server> Update(Guid projectId, Guid serverId, ServerUpdateParams updateParams)
    {
        await VerifyUserPermission(projectId, Permissions.ServerWrite);
        return await _serverService.Update(projectId, serverId, updateParams);
    }
    
    [HttpGet("{serverId:guid}")]
    public async Task<ServerData> Get(Guid projectId, Guid serverId)
    {
        await VerifyUserPermission(projectId, Permissions.ProjectRead);
        var ret = await List(projectId, serverId);
        return ret.Single();
    }

    [HttpDelete("{serverId:guid}")]
    public async Task Delete(Guid projectId, Guid serverId)
    {
        await VerifyUserPermission(projectId, Permissions.ProjectRead);

        var server = await VhContext.Servers
            .Where(server => server.ProjectId == projectId && !server.IsDeleted)
            .SingleAsync(server => server.ServerId == serverId);

        server.IsDeleted = true;
        await VhContext.SaveChangesAsync();
    }

    [HttpGet]
    public async Task<ServerData[]> List(
        Guid projectId, Guid? serverId = null, Guid? serverFarmId = null,
        int recordIndex = 0, int recordCount = 1000)
    {
        await VerifyUserPermission(projectId, Permissions.ProjectRead);
        var ret = await _serverService.List(projectId, serverId: serverId, serverFarmId: serverFarmId, recordIndex, recordCount);
        return ret;
    }

    [HttpPost("{serverId:guid}/reconfigure")]
    public async Task Reconfigure(Guid projectId, Guid serverId)
    {
        await VerifyUserPermission(projectId, Permissions.ServerWrite);

        // validate
        var server = await VhContext.Servers
            .Where(x => x.ProjectId == projectId && !x.IsDeleted)
            .SingleAsync(x => x.ServerId == serverId);

        server.ConfigCode = Guid.NewGuid();
        await VhContext.SaveChangesAsync();
        await _agentCacheClient.InvalidateServer(server.ServerId);
    }


    private static async Task<string> ExecuteSshCommand(Renci.SshNet.SshClient sshClient, string command, string? password, TimeSpan timeout)
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
    public async Task InstallBySshUserPassword(Guid projectId, Guid serverId, ServerInstallBySshUserPasswordParams installParams)
    {
        await VerifyUserPermission(projectId, Permissions.ServerInstall);

        var hostPort = installParams.HostPort == 0 ? 22 : installParams.HostPort;
        var connectionInfo = new Renci.SshNet.ConnectionInfo(installParams.HostName, hostPort, installParams.UserName, new Renci.SshNet.PasswordAuthenticationMethod(installParams.UserName, installParams.Password));

        var appSettings = await GetInstallAppSettings(projectId, serverId);
        await InstallBySsh(appSettings, connectionInfo, installParams.Password);
    }

    [HttpPost("{serverId:guid}/install-by-ssh-user-key")]
    public async Task InstallBySshUserKey(Guid projectId, Guid serverId, ServerInstallBySshUserKeyParams installParams)
    {
        await VerifyUserPermission(projectId, Permissions.ServerInstall);

        await using var keyStream = new MemoryStream(installParams.UserKey);
        using var privateKey = new Renci.SshNet.PrivateKeyFile(keyStream, installParams.UserKeyPassphrase);
        SShNet.Hack.RsaSha256Util.ConvertToKeyWithSha256Signature(privateKey); //todo: remove after SShNet get fixed

        var connectionInfo = new Renci.SshNet.ConnectionInfo(installParams.HostName, installParams.HostPort, installParams.UserName, new Renci.SshNet.PrivateKeyAuthenticationMethod(installParams.UserName, privateKey));

        var appSettings = await GetInstallAppSettings(projectId, serverId);
        await InstallBySsh(appSettings, connectionInfo, null);
    }

    private async Task InstallBySsh(ServerInstallAppSettings appSettings, Renci.SshNet.ConnectionInfo connectionInfo, string? userPassword)
    {
        using var sshClient = new Renci.SshNet.SshClient(connectionInfo);
        sshClient.Connect();

        var linuxCommand = GetInstallScriptForLinux(appSettings, false);

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

    [HttpGet("{serverId:guid}/install/manual")]
    public async Task<ServerInstallManual> GetInstallManual(Guid projectId, Guid serverId)
    {
        await VerifyUserPermission(projectId, Permissions.ServerReadConfig);

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
        var server = await VhContext.Servers
            .Where(server => server.ProjectId == projectId && !server.IsDeleted)
            .SingleAsync(server => server.ServerId == serverId);

        // create jwt
        var authorization = await _agentSystemClient.GetServerAgentAuthorization(server.ServerId);
        var appSettings = new ServerInstallAppSettings(new HttpAccessServerOptions(_appOptions.AgentUrl, authorization), server.Secret);
        return appSettings;
    }

    private string GetInstallScriptForLinux(ServerInstallAppSettings installAppSettings, bool manual)
    {
        var autoCommand = manual ? "" : "-q -autostart ";

        var script =
            "sudo su -c \"bash <( wget -qO- https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodServer-linux-x64.sh) " +
            autoCommand +
            $"-secret '{Convert.ToBase64String(installAppSettings.Secret)}' " +
            $"-restBaseUrl '{installAppSettings.HttpAccessServer.BaseUrl}' " +
            $"-restAuthorization '{installAppSettings.HttpAccessServer.Authorization}'\"";

        return script;
    }

    private string GetInstallScriptForWindows(ServerInstallAppSettings installAppSettings, bool manual)
    {
        var autoCommand = manual ? "" : "-q -autostart ";

        var script =
            "[Net.ServicePointManager]::SecurityProtocol = \"Tls,Tls11,Tls12\";" +
            "& ([ScriptBlock]::Create((Invoke-WebRequest(\"https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodServer-win-x64.ps1\")))) " +
            autoCommand +
            $"-secret \"{Convert.ToBase64String(installAppSettings.Secret)}\" " +
            $"-restBaseUrl \"{installAppSettings.HttpAccessServer.BaseUrl}\" " +
            $"-restAuthorization \"{installAppSettings.HttpAccessServer.Authorization}\"";

        return script;
    }


    [HttpGet("status-summary")]
    public async Task<ServersStatusSummary> GetStatusSummary(Guid projectId)
    {
        await VerifyUserPermission(projectId, Permissions.ProjectRead);

        // no lock
        await using var trans = await VhContext.WithNoLockTransaction();

        var query =
            from server in VhContext.Servers
            join serverStatus in VhContext.ServerStatuses on
                new { key1 = server.ServerId, key2 = true } equals new
                { key1 = serverStatus.ServerId, key2 = serverStatus.IsLast } into g0
            from serverStatus in g0.DefaultIfEmpty()
            where server.ProjectId == projectId && !server.IsDeleted
            select server.ToDto(
                null,
                serverStatus != null ? serverStatus.ToDto() : null,
                _appOptions.LostServerThreshold);

        // update model ServerStatusEx
        var servers = await query
            .ToArrayAsync();

        // update status from cache
        var cachedServers = await _agentCacheClient.GetServers(projectId);
        ServerUtil.UpdateByCache(servers, cachedServers);

        // create usage summary
        var usageSummary = new ServersStatusSummary
        {
            TotalServerCount = servers.Length,
            NotInstalledServerCount = servers.Count(x => x.ServerStatus is null),
            ActiveServerCount = servers.Count(x => x.ServerState is ServerState.Active),
            IdleServerCount = servers.Count(x => x.ServerState is ServerState.Idle),
            LostServerCount = servers.Count(x => x.ServerState is ServerState.Lost),
            SessionCount = servers.Where(x => x.ServerState is ServerState.Active).Sum(x => x.ServerStatus!.SessionCount),
            TunnelSendSpeed = servers.Where(x => x.ServerState is ServerState.Active).Sum(x => x.ServerStatus!.TunnelSendSpeed),
            TunnelReceiveSpeed = servers.Where(x => x.ServerState == ServerState.Active).Sum(x => x.ServerStatus!.TunnelReceiveSpeed),
        };

        return usageSummary;
    }

    [HttpGet("status-history")]
    public async Task<ServerStatusHistory[]> GetStatusHistory(Guid projectId, DateTime? usageBeginTime, DateTime? usageEndTime = null,
        Guid? serverId = null)
    {
        if (usageBeginTime == null) throw new ArgumentNullException(nameof(usageBeginTime));
        await VerifyUserPermission(projectId, Permissions.ProjectRead);
        await VerifyUsageQueryPermission(projectId, usageBeginTime, usageEndTime);

        var ret =
            await _usageReportService.GetServersStatusHistory(projectId, usageBeginTime.Value, usageEndTime, serverId);
        return ret;
    }
}