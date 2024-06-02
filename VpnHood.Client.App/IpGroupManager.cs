using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Logging;
using VpnHood.Common.Net;
using VpnHood.Common.Utils;

namespace VpnHood.Client.App;

public class IpGroupManager
{
    private string[]? _ipGroupIds;
    private readonly Dictionary<string, IpRangeOrderedList> _ipGroupIpRanges = new();

    private string IpGroupsFolderPath => Path.Combine(StorageFolder, "ipgroups");
    private string VersionFilePath => Path.Combine(StorageFolder, "version.txt");
    private string GetIpGroupFilePath(string ipGroup) => Path.Combine(IpGroupsFolderPath, ipGroup + ".ips");
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
        var ipGroups = await LoadIp2Location(ipLocationsStream).VhConfigureAwait();

        // Building the IpGroups directory structure
        VhLogger.Instance.LogTrace("Building the IpGroups directory structure...");
        Directory.CreateDirectory(IpGroupsFolderPath);
        foreach (var ipGroupIpRange in ipGroups)
        {
            var ipRanges = new IpRangeOrderedList(ipGroupIpRange.Value);
            await ipRanges.Save(GetIpGroupFilePath(ipGroupIpRange.Key));
        }

        // write version
        await File.WriteAllTextAsync(VersionFilePath, newVersion).VhConfigureAwait();
        _ipGroupIpRanges.Clear();
    }

    private static async Task<Dictionary<string, List<IpRange>>> LoadIp2Location(Stream ipLocationsStream)
    {
        // extract IpGroups
        var ipGroupIpRanges = new Dictionary<string, List<IpRange>>();
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
            if (!ipGroupIpRanges.TryGetValue(ipGroupId, out var ipRanges))
            {
                ipRanges = [];
                ipGroupIpRanges.Add(ipGroupId, ipRanges);
            }

            var addressFamily = items[0].Length > 10 || items[1].Length > 10 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
            var ipRange = new IpRange(
                IPAddressUtil.FromBigInteger(BigInteger.Parse(items[0]), addressFamily),
                IPAddressUtil.FromBigInteger(BigInteger.Parse(items[1]), addressFamily));

            ipRanges.Add(ipRange.IsIPv4MappedToIPv6 ? ipRange.MapToIPv4() : ipRange);
        }

        return ipGroupIpRanges;
    }

    public Task<string[]> GetIpGroupIds()
    {
        if (!Directory.Exists(IpGroupsFolderPath))
            return Task.FromResult(Array.Empty<string>());
        
        _ipGroupIds ??= Directory.GetFiles(IpGroupsFolderPath, "*.ips").Select(Path.GetFileNameWithoutExtension).ToArray();
        return Task.FromResult(_ipGroupIds);
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

        try
        {
            var filePath = GetIpGroupFilePath(ipGroupId);
            return await IpRangeOrderedList.Load(filePath);
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError(ex, "Could not load ip ranges for {IpGroupId}", ipGroupId);
            return IpRangeOrderedList.Empty;
        }
    }

    // it is sequential search
    public async Task<IpGroup?> FindIpGroup(IPAddress ipAddress, string? lastIpGroupId)
    {
        // IpGroup
        if (lastIpGroupId != null)
        {
            var ipRanges = await GetIpRanges(lastIpGroupId).VhConfigureAwait();
            if (ipRanges.Any(x => x.IsInRange(ipAddress)))
            {
                _ipGroupIpRanges.TryAdd(lastIpGroupId, ipRanges);
                return new IpGroup
                {
                    IpGroupId = lastIpGroupId,
                    IpRanges = ipRanges
                };
            }
        }

        // iterate through all groups
        var ipGroupIds = await GetIpGroupIds();
        foreach (var ipGroupId in ipGroupIds)
        {
            var ipRanges = await GetIpRanges(ipGroupId).VhConfigureAwait();
            if (ipRanges.Any(x => x.IsInRange(ipAddress)))
            {
                _ipGroupIpRanges.TryAdd(ipGroupId, ipRanges);
                return new IpGroup
                {
                    IpGroupId = ipGroupId,
                    IpRanges = ipRanges
                };
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
}