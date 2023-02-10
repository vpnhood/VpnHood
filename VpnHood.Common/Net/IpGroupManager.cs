using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Logging;

namespace VpnHood.Common.Net;

public class IpGroupManager
{
    private readonly string _ipGroupsFilePath;

    public IpGroup[] IpGroups { get; private set; } = Array.Empty<IpGroup>();
    private readonly Dictionary<IpRange, IpGroup> _ipRangeGroups = new();
    private IpRange[]? _sortedIpRanges;

    public IpGroupManager(string ipGroupsFilePath)
    {
        _ipGroupsFilePath = ipGroupsFilePath;
        try
        {
            IpGroups = JsonSerializer.Deserialize<IpGroup[]>(File.ReadAllText(ipGroupsFilePath))
                       ?? throw new FormatException($"Could deserialize {ipGroupsFilePath}!");
        }
        catch
        {
            // ignored
        }
    }

    private string IpGroupsFolderPath => Path.Combine(Path.GetDirectoryName(_ipGroupsFilePath)!, "ipgroups");

    public async Task AddFromIp2Location(Stream ipLocationsStream)
    {
        // extract IpGroups
        Dictionary<string, IpGroupNetwork> ipGroupNetworks = new();
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

                ipGroupNetwork = new IpGroupNetwork(ipGroupId, ipGroupName);
                ipGroupNetworks.Add(ipGroupId, ipGroupNetwork);
            }

            var ip1 = new IPAddress(BigInteger.Parse(items[0]).ToByteArray(true, true));
            var ip2 = new IPAddress(BigInteger.Parse(items[1]).ToByteArray(true, true));
            var ipRange = new IpRange(ip1, ip2);
            ipGroupNetwork.IpRanges.Add(ipRange);
        }

        //generating files
        VhLogger.Instance.LogTrace($"Generating IpGroups files. IpGroupCount: {ipGroupNetworks.Count}");
        Directory.CreateDirectory(IpGroupsFolderPath);
        foreach (var item in ipGroupNetworks)
        {
            var ipGroup = item.Value;
            var filePath = Path.Combine(IpGroupsFolderPath, $"{ipGroup.IpGroupId}.json");
            await using var fileStream = File.Create(filePath);
            await JsonSerializer.SerializeAsync(fileStream, ipGroup.IpRanges);
        }

        // creating IpGroups
        IpGroups = IpGroups.Concat(ipGroupNetworks.Values.Select(x => new IpGroup(x.IpGroupId, x.IpGroupName)))
            .ToArray();
        _sortedIpRanges = null;

        // save
        await File.WriteAllTextAsync(_ipGroupsFilePath, JsonSerializer.Serialize(IpGroups));
    }

    public async Task<IpRange[]> GetIpRanges(string ipGroupId)
    {
        var filePath = Path.Combine(IpGroupsFolderPath, $"{ipGroupId}.json");
        var json = await File.ReadAllTextAsync(filePath);
        return JsonSerializer.Deserialize<IpRange[]>(json) ?? throw new Exception($"Could not deserialize {filePath}!");
    }

    private readonly SemaphoreSlim _sortedIpRangesSemaphore = new(1, 1);
    private async Task LoadIpRangeGroups()
    {
        // load all groups
        try
        {
            await _sortedIpRangesSemaphore.WaitAsync();
            _ipRangeGroups.Clear();
            List<IpRange> ipRanges = new();
            foreach (var ipGroup in IpGroups)
            foreach (var ipRange in await GetIpRanges(ipGroup.IpGroupId))
            {
                ipRanges.Add(ipRange);
                _ipRangeGroups.Add(ipRange, ipGroup);
            }
            _sortedIpRanges = IpRange.Sort(ipRanges, false).ToArray();
        }
        finally
        {
            _sortedIpRangesSemaphore.Release();
        }
    }

    public async Task<IpGroup?> FindIpGroup(IPAddress ipAddress)
    {
        await LoadIpRangeGroups();
        var findIpRange = IpRange.FindRangeFast(_sortedIpRanges!, ipAddress);
        return findIpRange != null ? _ipRangeGroups[findIpRange] : null;
    }

    private class IpGroupNetwork : IpGroup
    {
        public IpGroupNetwork(string ipGroupId, string ipGroupName)
            : base(ipGroupId, ipGroupName)
        {
        }

        public List<IpRange> IpRanges { get; } = new();
    }
}