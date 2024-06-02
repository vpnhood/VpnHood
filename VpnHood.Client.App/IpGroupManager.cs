using System.Collections.Concurrent;
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
    private IpGroup[]? _ipGroups; //todo: just id enough
    private readonly ConcurrentDictionary<string, IpRangeOrderedList> _ipGroupIpRanges = new();

    private string IpGroupsFolderPath => Path.Combine(StorageFolder, "ipgroups");
    private string IpGroupsFilePath => Path.Combine(StorageFolder, "ipgroups.json");
    private string VersionFilePath => Path.Combine(StorageFolder, "version.txt");
    private string GetIpGroupFilePath(string ipGroup) => Path.Combine(IpGroupsFolderPath, ipGroup + ".json");
    public string StorageFolder { get; }
    public bool IsEmpty => _ipGroups == null || _ipGroups.Length == 0;

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
                oldVersion = await File.ReadAllTextAsync(VersionFilePath).VhConfigureAwait();
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
        var ipGroupNetworks = await LoadIp2Location(ipLocationsStream).VhConfigureAwait();

        // Building the IpGroups directory structure
        VhLogger.Instance.LogTrace("Building the IpGroups directory structure...");
        Directory.CreateDirectory(IpGroupsFolderPath);
        foreach (var ipGroupNetwork in ipGroupNetworks)
        {
            ipGroupNetwork.Value.IpRanges = ipGroupNetwork.Value.IpRanges.ToArray().Sort().ToList();
            await File.WriteAllTextAsync(GetIpGroupFilePath(ipGroupNetwork.Key), JsonSerializer.Serialize(ipGroupNetwork.Value.IpRanges)).VhConfigureAwait();
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
        await File.WriteAllTextAsync(IpGroupsFilePath, JsonSerializer.Serialize(ipGroups)).VhConfigureAwait();

        // write version
        await File.WriteAllTextAsync(VersionFilePath, newVersion).VhConfigureAwait();
        _ipGroups = null; // clear cache
        _ipGroupIpRanges.Clear();
    }

    private static async Task<Dictionary<string, IpGroupNetwork>> LoadIp2Location(Stream ipLocationsStream)
    {
        // extract IpGroups
        var ipGroupNetworks = new Dictionary<string, IpGroupNetwork>();
        using var streamReader = new StreamReader(ipLocationsStream);
        while (!streamReader.EndOfStream)
        {
            var line = await streamReader.ReadLineAsync().VhConfigureAwait();
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

        var json = await File.ReadAllTextAsync(IpGroupsFilePath).VhConfigureAwait();
        _ipGroups = VhUtil.JsonDeserialize<IpGroup[]>(json);
        return _ipGroups;
    }

    public async Task<IpRangeOrderedList> GetIpRanges(string ipGroupId)
    {
        var ipRanges = await GetIpRangesInternal(ipGroupId).VhConfigureAwait();
        _ipGroupIpRanges.TryAdd(ipGroupId, ipRanges);
        return ipRanges;
    }

    private async Task<IpRangeOrderedList> GetIpRangesInternal(string ipGroupId)
    {
        if (_ipGroupIpRanges.TryGetValue(ipGroupId, out var ipGroupRangeCache))
            return ipGroupRangeCache;

        var filePath = GetIpGroupFilePath(ipGroupId);
        var json = await File.ReadAllTextAsync(filePath).VhConfigureAwait();
        var ipRanges = JsonSerializer.Deserialize<IpRange[]>(json) ?? throw new Exception($"Could not deserialize {filePath}!");
        var ip4MappedRanges = ipRanges.Where(x => x.AddressFamily == AddressFamily.InterNetwork).Select(x => x.MapToIPv6());
        var ret = new IpRangeOrderedList(ipRanges.Concat(ip4MappedRanges));
        return ret;
    }

    // it is sequential search
    public async Task<IpGroup?> FindIpGroup(IPAddress ipAddress, string? lastIpGroupId)
    {
        var ipGroups = await GetIpGroups().VhConfigureAwait();
        var lastIpGroup = ipGroups.FirstOrDefault(x => x.IpGroupId == lastIpGroupId);

        // IpGroup
        if (lastIpGroup != null)
        {
            var ipRanges = await GetIpRanges(lastIpGroup.IpGroupId).VhConfigureAwait();
            if (ipRanges.Any(x => x.IsInRange(ipAddress)))
            {
                _ipGroupIpRanges.TryAdd(lastIpGroup.IpGroupId, ipRanges);
                return lastIpGroup;
            }
        }

        // iterate through all groups
        foreach (var ipGroup in ipGroups)
        {
            var ipRanges = await GetIpRanges(ipGroup.IpGroupId).VhConfigureAwait();
            if (ipRanges.Any(x => x.IsInRange(ipAddress)))
            {
                _ipGroupIpRanges.TryAdd(ipGroup.IpGroupId, ipRanges);
                return ipGroup;
            }
        }

        return null;
    }

    public async Task<string?> GetCountryCodeByCurrentIp()
    {
        try
        {
            var ipAddress =
                await IPAddressUtil.GetPublicIpAddress(AddressFamily.InterNetwork).VhConfigureAwait() ??
                await IPAddressUtil.GetPublicIpAddress(AddressFamily.InterNetworkV6).VhConfigureAwait();

            if (ipAddress == null)
                return null;

            var ipGroup = await FindIpGroup(ipAddress, null).VhConfigureAwait();
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