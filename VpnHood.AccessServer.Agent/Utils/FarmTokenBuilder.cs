using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using GrayMint.Common.Utils;
using VpnHood.AccessServer.Persistence.Enums;
using VpnHood.AccessServer.Persistence.Models;
using VpnHood.Common;
using VpnHood.Common.Utils;

namespace VpnHood.AccessServer.Agent.Utils;

public static class FarmTokenBuilder
{
    public static bool UpdateIfChanged(ServerFarmModel serverFarm)
    {
        try {
            return UpdateIfChangedInternal(serverFarm);
        }
        catch (Exception ex) {
            var isChanged = serverFarm.TokenJson != null || serverFarm.TokenError != ex.Message; 
            // serverFarm.TokenJson = null; Don't set to null and let leave the old token intact
            serverFarm.TokenError = ex.Message;
            return isChanged;
        }
    }

    private static bool UpdateIfChangedInternal(ServerFarmModel serverFarm)
    {
        // build new host token
        var farmTokenNew = Build(serverFarm);

        // check for change
        if (!string.IsNullOrEmpty(serverFarm.TokenJson)) {
            var farmTokenOld = JsonSerializer.Deserialize<ServerToken>(serverFarm.TokenJson);
            if (farmTokenOld != null && !farmTokenOld.IsTokenUpdated(farmTokenNew))
                return false;
        }

        // update host token
        serverFarm.TokenJson = JsonSerializer.Serialize(farmTokenNew);
        serverFarm.TokenError = null;
        return true;
    }


    public static ServerToken Build(ServerFarmModel serverFarm)
    {
        ArgumentNullException.ThrowIfNull(serverFarm.Servers);
        ArgumentNullException.ThrowIfNull(serverFarm.TokenRepos);
        var certificate = serverFarm.GetCertificateInToken();
        var x509Certificate = new X509Certificate2(certificate.RawData);

        var servers = serverFarm.Servers!
            .Where(server => server.IsEnabled)
            .ToArray();

        // find all public accessPoints 
        var accessPoints = servers
            .Where(server => server is { IsEnabled: true, IsDeleted: false })
            .SelectMany(server => server.AccessPoints)
            .Where(accessPoint => accessPoint.AccessPointMode == AccessPointMode.PublicInToken)
            .ToArray();

        // find all token tcp port
        var hostPort = 0;
        if (serverFarm.UseHostName) {
            var hostPorts = accessPoints.DistinctBy(x => x.TcpPort).ToArray();
            hostPort = hostPorts.FirstOrDefault()?.TcpPort ?? 443;
            if (hostPorts.Any(x => x.TcpPort != hostPort))
                throw new InvalidOperationException
                    ("All PublicInToken access points must use the same port when using valid domain.");

            if (hostPort < 0)
                throw new InvalidOperationException("The host port must be greater than 0 when using valid domain.");
        }
        else if (accessPoints.Length == 0) {
            throw new InvalidOperationException(
                "The farm must have at least one server with PublicInToken access points.");
        }

        // serverLocations
        var locations = servers
            .Where(server => server.Location != null)
            .Select(x => ServerLocationInfo.Parse(x.Location!.ToPath()))
            .Distinct()
            .Order()
            .Select(x => x.ServerLocation)
            .ToArray();

        // create token
        var serverToken = new ServerToken {
            CertificateHash = certificate.IsValidated ? null : x509Certificate.GetCertHash(),
            HostName = x509Certificate.GetNameInfo(X509NameType.DnsName, false),
            HostEndPoints = accessPoints
                .Select(accessPoint => new IPEndPoint(accessPoint.IpAddress, accessPoint.TcpPort)).ToArray(),
            Secret = serverFarm.Secret,
            HostPort = hostPort,
            IsValidHostName = serverFarm.UseHostName,
            Url = serverFarm.TokenUrl,
            CreatedTime = VhUtil.RemoveMilliseconds(DateTime.UtcNow),
            ServerLocations = locations.Length > 0 ? locations : null
        };

        // validate token

        return serverToken;
    }

    public static ServerToken? GetServerToken(string? serverFarmTokenJson)
    {
        try {
            return serverFarmTokenJson != null ? GmUtil.JsonDeserialize<ServerToken>(serverFarmTokenJson) : null;
        }
        catch {
            return null;
        }
    }

    public static ServerToken GetRequiredServerToken(string? serverFarmTokenJson)
    {
        if (string.IsNullOrEmpty(serverFarmTokenJson))
            throw new InvalidOperationException("The server farm token has not been initialized properly.");

        return GmUtil.JsonDeserialize<ServerToken>(serverFarmTokenJson);
    }
}