using System.IO.Compression;
using System.Net.Sockets;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.IpLocations.Providers.Offlines;

public static class Ip2LocationDbParser
{
    public static async Task UpdateLocalDb(string filePath, string apiKey, bool forIpRange, TimeSpan? interval = null,
        CancellationToken cancellationToken = default)
    {
        interval ??= TimeSpan.FromDays(7);
        if (File.GetLastWriteTime(filePath) > DateTime.Now - interval)
            return;

        // copy zip to memory
        using var httpClient = new HttpClient();
        // ReSharper disable once StringLiteralTypo
        var url = $"https://www.ip2location.com/download/?token={apiKey}&file=DB1LITECSVIPV6";
        await using var ipLocationZipNetStream = await httpClient.GetStreamAsync(url, cancellationToken);
        using var ipLocationZipStream = new MemoryStream();
        await ipLocationZipNetStream.CopyToAsync(ipLocationZipStream, cancellationToken);
        ipLocationZipStream.Position = 0;

        // build new ipLocation filePath
        await using var ipLocationZipArchive = new ZipArchive(ipLocationZipStream, ZipArchiveMode.Read);
        const string entryName = "IP2LOCATION-LITE-DB1.IPV6.CSV";
        var ipLocationEntry = ipLocationZipArchive.GetEntry(entryName) ??
                              throw new Exception($"{entryName} not found in the zip file!");

        await using var crvStream = await ipLocationEntry.OpenAsync(cancellationToken);
        if (forIpRange)
            await BuildLocalIpRangeLocationDb(crvStream, filePath, cancellationToken);
        else
            await BuildLocalIpLocationDb(crvStream, filePath, cancellationToken);
    }

    public static async Task BuildLocalIpRangeLocationDb(Stream crvStream, string outputZipFile,
        CancellationToken cancellationToken)
    {
        var countryIpRanges = await ParseIp2LocationCrv(crvStream, cancellationToken).Vhc();

        // Building the IpGroups directory structure
        VhLogger.Instance.LogDebug("Building the optimized Ip2Location archive...");
        await using var outputStream = File.Create(outputZipFile);
        await using var newArchive = new ZipArchive(outputStream, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var countryIpRange in countryIpRanges.OrderBy(x => x.Key)) {
            var ipRanges = new IpRangeOrderedList(countryIpRange.Value);
            var entry = newArchive.CreateEntry($"{countryIpRange.Key}.ips", CompressionLevel.NoCompression);
            await using var entryStream = await entry.OpenAsync(cancellationToken);
            ipRanges.Serialize(entryStream);
        }

        // add checksum file
        var checksumEntry = newArchive.CreateEntry("_checksum.txt", CompressionLevel.NoCompression);
        await using var checksumStream = await checksumEntry.OpenAsync(cancellationToken);
        var checksum = ComputeChecksum(countryIpRanges);
        await using var writer = new StreamWriter(checksumStream, Encoding.ASCII, leaveOpen: true);
        await writer.WriteAsync(checksum.AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
    }

    public static async Task BuildLocalIpLocationDb(Stream crvStream, string outputFile,
        CancellationToken cancellationToken)
    {
        var countries = await ParseIp2LocationCrv(crvStream, cancellationToken).Vhc();

        // Building the IpGroups directory structure
        VhLogger.Instance.LogDebug("Building the optimized Ip2Location archive...");
        var ipRangeInfos = new List<LocalIpLocationProvider.IpRangeInfo>();
        foreach (var country in countries) {
            cancellationToken.ThrowIfCancellationRequested();
            ipRangeInfos.AddRange(
                country.Value.Select(ipRange => new LocalIpLocationProvider.IpRangeInfo {
                    CountryCode = country.Key,
                    IpRanges = ipRange
                }));
        }

        // write to file
        await using var outputStream = File.Create(outputFile);
        var ipRangeLocationProvider = new LocalIpLocationProvider(ipRangeInfos);
        ipRangeLocationProvider.Serialize(outputStream);
    }

    public static async Task<Dictionary<string, List<IpRange>>> ParseIp2LocationCrv(Stream ipLocationsCrvStream,
        CancellationToken cancellationToken)
    {
        // extract IpGroups
        var ipGroupIpRanges = new Dictionary<string, List<IpRange>>();
        using var streamReader = new StreamReader(ipLocationsCrvStream);
        while (!streamReader.EndOfStream) {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await streamReader.ReadLineAsync(cancellationToken).Vhc() ?? "";
            var items = line.Replace("\"", "").Split(',');
            if (items.Length != 4)
                continue;

            var ipGroupId = items[2].ToLower();
            if (ipGroupId == "-") continue;
            if (ipGroupId == "um") ipGroupId = "us";
            if (!ipGroupIpRanges.TryGetValue(ipGroupId, out var ipRanges)) {
                ipRanges = [];
                ipGroupIpRanges.Add(ipGroupId, ipRanges);
            }

            var addressFamily = items[0].Length > 10 || items[1].Length > 10
                ? AddressFamily.InterNetworkV6
                : AddressFamily.InterNetwork;

            var ipRange = new IpRange(
                IPAddressUtil.FromBigInteger(BigInteger.Parse(items[0]), addressFamily),
                IPAddressUtil.FromBigInteger(BigInteger.Parse(items[1]), addressFamily));

            ipRanges.Add(ipRange.IsIPv4MappedToIPv6 ? ipRange.MapToIPv4() : ipRange);
        }

        return ipGroupIpRanges;
    }

    private static string ComputeChecksum(Dictionary<string, List<IpRange>> countryIpRanges)
    {
        using var md5 = MD5.Create();

        foreach (var country in countryIpRanges.OrderBy(x => x.Key)) {
            var countryBytes = Encoding.ASCII.GetBytes(country.Key);
            md5.TransformBlock(countryBytes, 0, countryBytes.Length, null, 0);

            var orderedRanges = new IpRangeOrderedList(country.Value);
            foreach (var ipRange in orderedRanges) {
                var firstBytes = ipRange.FirstIpAddress.GetAddressBytes();
                var lastBytes = ipRange.LastIpAddress.GetAddressBytes();
                md5.TransformBlock(firstBytes, 0, firstBytes.Length, null, 0);
                md5.TransformBlock(lastBytes, 0, lastBytes.Length, null, 0);
            }
        }

        md5.TransformFinalBlock([], 0, 0);
        return Convert.ToBase64String(md5.Hash!);
    }
}