using System.Net;
using System.Security.Cryptography.X509Certificates;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Models;
using VpnHood.Common.Utils;
using VpnHood.Common;
using System.Text.Json;

namespace VpnHood.AccessServer.Utils;

public static class AccessUtil
{
    public static string ValidateIpEndPoint(string ipEndPoint)
    {
        return IPEndPoint.Parse(ipEndPoint).ToString();
    }

    public static string ValidateIpAddress(string ipAddress)
    {
        return IPAddress.Parse(ipAddress).ToString();
    }

    public static string FindUniqueName(string template, string?[] names)
    {
        for (var i = 2; ; i++)
        {
            var name = template.Replace("##", i.ToString());
            if (names.All(x => x != name))
                return name;
        }
    }

    public static string? GenerateCacheKey(string keyBase, DateTime? beginTime, DateTime? endTime, out TimeSpan? cacheExpiration)
    {
        cacheExpiration = null;
        if (endTime != null && DateTime.UtcNow - endTime >= TimeSpan.FromMinutes(5))
            return null;

        var duration = (endTime ?? DateTime.UtcNow) - (beginTime ?? DateTime.UtcNow.AddYears(-2));
        var threshold = (long)(duration.TotalMinutes / 30);
        var cacheKey = $"{keyBase}_{threshold}";
        cacheExpiration = TimeSpan.FromMinutes(Math.Min(60 * 24, duration.TotalMinutes / 30));
        return cacheKey;
    }

    public static bool FarmTokenUpdateIfChanged(ServerFarmModel serverFarm)
    {
        // build new host token
        var farmTokenNew = FarmTokenBuild(serverFarm);

        // check for change
        if (!string.IsNullOrEmpty(serverFarm.FarmTokenJson))
        {
            var farmTokenOld = JsonSerializer.Deserialize<ServerToken>(serverFarm.FarmTokenJson);
            if (farmTokenOld != null && !farmTokenOld.IsTokenUpdated(farmTokenNew))
                return false;
        }

        // update host token
        serverFarm.FarmTokenJson = JsonSerializer.Serialize(farmTokenNew);
        return true;
    }

    public static ServerToken FarmTokenBuild(ServerFarmModel serverFarm)
    {
        ArgumentNullException.ThrowIfNull(serverFarm.Servers);
        ArgumentNullException.ThrowIfNull(serverFarm.CertificateId);

        // find all public accessPoints 
        var accessPoints = serverFarm.Servers!
            .SelectMany(server => server.AccessPoints)
            .Where(accessPoint => accessPoint.AccessPointMode == AccessPointMode.PublicInToken)
            .ToArray();

        // find all token tcp port
        var hostPort = 0;
        if (serverFarm.UseHostName)
        {
            var hostPorts = accessPoints.DistinctBy(x => x.TcpPort).ToArray();
            if (hostPorts.Length > 1)
                throw new Exception(
                    $"More than one TCP port has been found in PublicInTokens. It is ambiguous as to which port should be used for the hostname. " +
                    $"EndPoints: {string.Join(',', hostPorts.Select(x => x.ToString()))}");
            hostPort = hostPorts.SingleOrDefault()?.TcpPort ?? 443;
        }

        // create token
        var x509Certificate = new X509Certificate2(serverFarm.Certificate!.RawData);
        var serverToken = new ServerToken
        {
            CertificateHash = x509Certificate.GetCertHash(),
            HostName = x509Certificate.GetNameInfo(X509NameType.DnsName, false),
            HostEndPoints = accessPoints.Select(accessPoint => new IPEndPoint(accessPoint.IpAddress, accessPoint.TcpPort)).ToArray(),
            Secret = serverFarm.Secret,
            HostPort = hostPort,
            IsValidHostName = serverFarm.UseHostName,
            Url = serverFarm.FarmTokenUrl,
            CreatedTime = VhUtil.RemoveMilliseconds(DateTime.UtcNow),
        };

        return serverToken;
    }
}