using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Renci.SshNet;
using VpnHood.AccessServer.DTOs;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Security;
using VpnHood.Common;
using VpnHood.Server.AccessServers;

namespace VpnHood.AccessServer.Controllers
{
    [Route("/api/projects/{projectId:guid}/servers")]
    public class ServerController : SuperController<ServerController>
    {
        public ServerController(ILogger<ServerController> logger) : base(logger)
        {
        }

        [HttpPost]
        public async Task<Models.Server> Create(Guid projectId, ServerCreateParams createParams)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.ServerWrite);

            // validate
            var accessControlGroup = createParams.AccessPointGroupId != null
                ? await vhContext.AccessPointGroups.SingleAsync(x => x.ProjectId == projectId && x.AccessPointGroupId == createParams.AccessPointGroupId)
                : null;

            var server = new Models.Server
            {
                ProjectId = projectId,
                ServerId = Guid.NewGuid(),
                CreatedTime = DateTime.UtcNow,
                ServerName = createParams.ServerName,
                Secret = Util.GenerateSessionKey(),
                AuthorizationCode = Guid.NewGuid(),
                AccessPointGroupId = accessControlGroup?.AccessPointGroupId
            };
            await vhContext.Servers.AddAsync(server);
            await vhContext.SaveChangesAsync();

            server.Secret = Array.Empty<byte>();
            return server;
        }

        [HttpPatch("{serverId:guid}")]
        public async Task<Models.Server> Update(Guid projectId, Guid serverId, ServerUpdateParams updateParams)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.ServerWrite);

            // validate
            var server = await vhContext.Servers
                .Include(x => x.AccessPoints)
                .SingleAsync(x => x.ProjectId == projectId && x.ServerId == serverId);

            if (updateParams.AccessPointGroupId != null)
            {
                var accessPointGroup = updateParams.AccessPointGroupId.Value != null
                    ? await vhContext.AccessPointGroups.SingleAsync(x => x.ProjectId == projectId && x.AccessPointGroupId == updateParams.AccessPointGroupId)
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

                    vhContext.AccessPoints.UpdateRange(server.AccessPoints);
                }
            }

            if (updateParams.ServerName != null) server.ServerName = updateParams.ServerName;
            if (updateParams.GenerateNewSecret?.Value == true) server.Secret = Util.GenerateSessionKey();
            await vhContext.SaveChangesAsync();

            server.Secret = Array.Empty<byte>();
            return server;
        }

        [HttpGet("{serverId:guid}/status-logs")]
        public async Task<ServerStatusEx[]> GetStatusLogs(Guid projectId, Guid serverId, int recordIndex = 0,
            int recordCount = 1000)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.ServerRead);

            var list = await vhContext.ServerStatus
                .Include(x => x.Server)
                .Where(x => x.Server!.ProjectId == projectId && x.Server.ServerId == serverId)
                .OrderByDescending(x => x.ServerStatusId)
                .Skip(recordIndex).Take(recordCount)
                .ToArrayAsync();

            return list;
        }

        [HttpGet("{serverId:guid}")]
        public async Task<ServerData> Get(Guid projectId, Guid serverId)
        {
            var res = await List(projectId, serverId);
            return res.Single();
        }

        [HttpGet]
        public async Task<ServerData[]> List(Guid projectId, Guid? serverId = null, int recordIndex = 0, int recordCount = 1000)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.ServerRead);

            var query =
                from server in vhContext.Servers
                join serverStatusLog in vhContext.ServerStatus on new { key1 = server.ServerId, key2 = true } equals
                    new { key1 = serverStatusLog.ServerId, key2 = serverStatusLog.IsLast } into grouping
                from serverStatusLog in grouping.DefaultIfEmpty()
                join accessPoint in vhContext.AccessPoints on server.ServerId equals accessPoint.ServerId into grouping2
                from accessPoint in grouping2.DefaultIfEmpty()
                join accessPointGroup in vhContext.AccessPointGroups on accessPoint.AccessPointGroupId equals accessPointGroup.AccessPointGroupId into grouping3
                from accessPointGroup in grouping3.DefaultIfEmpty()
                join accessPointGroup2 in vhContext.AccessPointGroups on server.AccessPointGroupId equals accessPointGroup2.AccessPointGroupId into grouping4
                from accessPointGroup2 in grouping4.DefaultIfEmpty()

                where server.ProjectId == projectId
                select new { server, serverStatusLog, accessPointGroup, accessPointGroup2, accessPoint };

            if (serverId != null)
                query = query.Where(x => x.server.ServerId == serverId);

            var res = await query
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
                    Status = x.serverStatusLog
                })
                .ToArray();

            // remove server secret
            foreach (var item in ret)
            {
                item.Server.Secret = Array.Empty<byte>();
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
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.ServerInstall);

            var hostPort = installParams.HostPort == 0 ? 22 : installParams.HostPort;
            var connectionInfo = new ConnectionInfo(installParams.HostName, hostPort, installParams.UserName, new PasswordAuthenticationMethod(installParams.UserName, installParams.Password));

            var appSettings = await GetInstallAppSettings(vhContext, projectId, serverId);
            await InstallBySsh(appSettings, connectionInfo, installParams.Password);
        }

        [HttpPost("{serverId:guid}/install-by-ssh-user-key")]
        public async Task InstallBySshUserKey(Guid projectId, Guid serverId, ServerInstallBySshUserKeyParams installParams)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.ServerInstall);

            await using var keyStream = new MemoryStream(installParams.UserKey);
            using var privateKey = new PrivateKeyFile(keyStream, installParams.UserKeyPassphrase);
            var connectionInfo = new ConnectionInfo(installParams.HostName, installParams.HostPort, installParams.UserName, new PrivateKeyAuthenticationMethod(installParams.UserName, privateKey));

            var appSettings = await GetInstallAppSettings(vhContext, projectId, serverId);
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
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.ServerReadConfig);

            var appSettings = await GetInstallAppSettings(vhContext, projectId, serverId);
            var ret = new ServerInstallManual(appSettings, GetInstallLinuxCommand(appSettings, true));
            return ret;
        }

        private async Task<ServerInstallAppSettings> GetInstallAppSettings(VhContext vhContext, Guid projectId, Guid serverId)
        {
            var server = await vhContext.Servers.SingleAsync(x => x.ProjectId == projectId && x.ServerId == serverId);
            var authItem = AccessServerApp.Instance.RobotAuthItem;

            var claims = new List<Claim>
            {
                new("authorization_code", server.AuthorizationCode.ToString()),
                new("usage_type", "agent"),
            };

            // create jwt
            var jwt = JwtTool.CreateSymmetricJwt(Convert.FromBase64String(authItem.SymmetricSecurityKey!),
                authItem.Issuers[0], authItem.ValidAudiences[0], serverId.ToString(), claims.ToArray());

            var port = Request.Host.Port ?? (Request.IsHttps ? 443 : 80);
            var uri = new UriBuilder(Request.Scheme, Request.Host.Host, port, "/api/agent/").Uri;

            var appSettings = new ServerInstallAppSettings(new RestAccessServerOptions(uri.AbsoluteUri, $"Bearer {jwt}"), server.Secret);
            return appSettings;
        }

        private string GetInstallLinuxCommand(ServerInstallAppSettings installAppSettings, bool manual)
        {
            var autoCommand = manual ? "" : "-q -autostart ";

            //todo: linux2
            var linuxCommand =
                "sudo su -c \"bash <( wget -qO- https://github.com/vpnhood/VpnHood/releases/latest/download/install-linux2.sh) " +
                autoCommand +
                $"-secret '{Convert.ToBase64String(installAppSettings.Secret)}' " +
                $"-restBaseUrl '{installAppSettings.RestAccessServer.BaseUrl}' " +
                $"-restAuthorization '{installAppSettings.RestAccessServer.Authorization}'\"";

            return linuxCommand;
        }
    }
}