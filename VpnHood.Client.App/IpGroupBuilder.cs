using System.IO.Compression;
using System.Net.Sockets;
using System.Numerics;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Logging;
using VpnHood.Common.Net;
using VpnHood.Common.Utils;

namespace VpnHood.Client.App;

public class IpGroupBuilder
{
    public static async Task BuildIpGroupArchiveFromIp2Location(Stream crvStream, string outputZipFile)
    {
        var ipGroups = await LoadIp2Location(crvStream).VhConfigureAwait();

        // Building the IpGroups directory structure
        VhLogger.Instance.LogTrace("Building the optimized Ip2Location archive...");
        await using var outputStream = File.OpenWrite(outputZipFile);
        using var newArchive = new ZipArchive(outputStream, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var ipGroup in ipGroups)
        {
            var ipRanges = new IpRangeOrderedList(ipGroup.Value);
            var entry = newArchive.CreateEntry($"{ipGroup.Key}.ips");
            await using var entryStream = entry.Open();
            ipRanges.Serialize(entryStream);
        }
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
}