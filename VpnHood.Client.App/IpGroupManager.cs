using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Logging;
using VpnHood.Common.Net;
using VpnHood.Common.Utils;

namespace VpnHood.Client.App;

public class IpGroupManager
{
    private IpGroup[]? _ipGroups;

    private string IpGroupsFolderPath => Path.Combine(StorageFolder, "ipgroups");
    private string IpGroupsFilePath => Path.Combine(StorageFolder, "ipgroups.json");
    private string VersionFilePath => Path.Combine(StorageFolder, "version.txt");
    private string GetIpGroupFilePath(string ipGroup) => Path.Combine(IpGroupsFolderPath, ipGroup + ".json");
    public string StorageFolder { get; }

    private IpGroupManager(string storageFolder)
    {
        StorageFolder = storageFolder;
    }

    public static Task<IpGroupManager> Create(string storageFolder)
    {
        var ret = new IpGroupManager(storageFolder);
        return Task.FromResult(ret);
    }

    public async Task InitByIp2LocationZipStream(ZipArchiveEntry archiveEntry)
    {
        var newVersion = archiveEntry.LastWriteTime.ToUniversalTime().ToString("u");
        var oldVersion = "NotFound";

        // check is version changed
        if (File.Exists(VersionFilePath))
        {
            try
            {
                oldVersion = await File.ReadAllTextAsync(VersionFilePath);
                if (oldVersion == newVersion)
                    return;
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogError(ex, "Could not read last version file. File: {File}", VersionFilePath);
            }
        }

        // Build new structure
        VhLogger.Instance.LogInformation("Building IPLocation. OldVersion: {OldVersion}, NewVersion {NewVersion},", oldVersion, newVersion);

        // delete all files and other versions if any
        if (Directory.Exists(IpGroupsFolderPath))
        {
            VhLogger.Instance.LogTrace("Deleting the old IpGroups...");
            Directory.Delete(IpGroupsFolderPath, true);
        }

        // Loading the ip2Location stream
        VhLogger.Instance.LogTrace("Loading the ip2Location stream...");
        await using var ipLocationsStream = archiveEntry.Open();
        var ipGroupNetworks = await LoadIp2Location(ipLocationsStream);

        // Building the IpGroups directory structure
        VhLogger.Instance.LogTrace("Building the IpGroups directory structure...");
        Directory.CreateDirectory(IpGroupsFolderPath);
        foreach (var ipGroupNetwork in ipGroupNetworks)
        {
            ipGroupNetwork.Value.IpRanges = ipGroupNetwork.Value.IpRanges.ToArray().Sort().ToList();
            await File.WriteAllTextAsync(GetIpGroupFilePath(ipGroupNetwork.Key), JsonSerializer.Serialize(ipGroupNetwork.Value.IpRanges));
        }

        // write IpGroups file
        var ipGroups = ipGroupNetworks.Select(x =>
                new IpGroup
                {
                    IpGroupId = x.Value.IpGroupId,
                    IpGroupName = x.Value.IpGroupName
                })
            .OrderBy(x => x.IpGroupName)
            .ToArray();
        await File.WriteAllTextAsync(IpGroupsFilePath, JsonSerializer.Serialize(ipGroups));

        // write version
        await File.WriteAllTextAsync(VersionFilePath, newVersion);
        _ipGroups = null; // clear cache
    }

    private static async Task<Dictionary<string, IpGroupNetwork>> LoadIp2Location(Stream ipLocationsStream)
    {
        // extract IpGroups
        var ipGroupNetworks = new Dictionary<string, IpGroupNetwork>();
        using var streamReader = new StreamReader(ipLocationsStream);
        while (!streamReader.EndOfStream)
        {
            var line = await streamReader.ReadLineAsync();
            var items = line.Replace("\"", "").Split(',');
            if (items.Length != 4)
                continue;

            var ipGroupId = items[2].ToLower();
            if (ipGroupId == "-") continue;
            if (ipGroupId == "um") ipGroupId = "us";
            if (!ipGroupNetworks.TryGetValue(ipGroupId, out var ipGroupNetwork))
            {
                var ipGroupName = ipGroupId switch
                {
                    "us" => "United States",
                    "gb" => "United Kingdom",
                    _ => items[3]
                };

                ipGroupName = Regex.Replace(ipGroupName, @"\(.*?\)", "").Replace("  ", " ");
                ipGroupNetwork = new IpGroupNetwork
                {
                    IpGroupId = ipGroupId,
                    IpGroupName = ipGroupName
                };

                ipGroupNetworks.Add(ipGroupId, ipGroupNetwork);
            }

            var addressFamily = items[0].Length > 10 || items[1].Length > 10 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
            var ipRange = new IpRange(
                IPAddressUtil.FromBigInteger(BigInteger.Parse(items[0]), addressFamily),
                IPAddressUtil.FromBigInteger(BigInteger.Parse(items[1]), addressFamily));

            ipGroupNetwork.IpRanges.Add(ipRange.IsIPv4MappedToIPv6 ? ipRange.MapToIPv4() : ipRange);
        }

        return ipGroupNetworks;
    }

    public async Task<IpGroup[]> GetIpGroups()
    {
        if (_ipGroups != null)
            return _ipGroups;

        // no countries if there is no import
        if (!File.Exists(IpGroupsFilePath))
            return [];

        var json = await File.ReadAllTextAsync(IpGroupsFilePath);
        _ipGroups =  VhUtil.JsonDeserialize<IpGroup[]>(json);
        return _ipGroups;
    }

    public async Task<IEnumerable<IpRange>> GetIpRanges(string ipGroupId)
    {
        var filePath = GetIpGroupFilePath(ipGroupId);
        var json = await File.ReadAllTextAsync(filePath);
        var ipRanges = JsonSerializer.Deserialize<IpRange[]>(json) ?? throw new Exception($"Could not deserialize {filePath}!");
        var ip4MappedRanges = ipRanges.Where(x => x.AddressFamily==AddressFamily.InterNetwork).Select(x => x.MapToIPv6());
        var ret = ipRanges.Concat(ip4MappedRanges);
        return ret;
    }

    // it is sequential search
    public async Task<IpGroup?> FindIpGroup(IPAddress ipAddress, string? lastIpGroupId)
    {
        var ipGroups = await GetIpGroups();
        var lastIpGroup = ipGroups.FirstOrDefault(x => x.IpGroupId == lastIpGroupId);

        // IpGroup
        if (lastIpGroup != null)
        {
            var ipRanges = await GetIpRanges(lastIpGroup.IpGroupId);
            if (ipRanges.Any(x => x.IsInRange(ipAddress)))
                return lastIpGroup;
        }

        // iterate through all groups
        foreach (var ipGroup in ipGroups)
        {
            var ipRanges = await GetIpRanges(ipGroup.IpGroupId);
            if (ipRanges.Any(x => x.IsInRange(ipAddress)))
                return ipGroup;
        }

        return null;
    }

    public async Task<string?> GetCountryCodeByCurrentIp()
    {
        try
        {
            var ipAddress =
                await IPAddressUtil.GetPublicIpAddress(AddressFamily.InterNetwork) ??
                await IPAddressUtil.GetPublicIpAddress(AddressFamily.InterNetworkV6);

            if (ipAddress == null)
                return null;

            var ipGroup = await FindIpGroup(ipAddress, null);
            return ipGroup?.IpGroupId;
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError(ex, "Could not retrieve client country from public ip services.");
            return null;
        }
    }

    private class IpGroupNetwork : IpGroup
    {
        public List<IpRange> IpRanges { get; set; } = [];
    }
}