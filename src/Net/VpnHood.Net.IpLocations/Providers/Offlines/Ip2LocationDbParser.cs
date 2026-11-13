using System.IO.Compression;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Assets;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Net.IpLocations.Providers.Offlines;

/// <summary>
/// Provides the means to build the local location databases from IP2Location's LITE DB1 CSV, which maps IP ranges to
/// countries.
/// </summary>
public static class Ip2LocationDbParser
{
    /// <summary>
    /// Downloads IP2Location's LITE DB1 database and rebuilds a local database from it, unless the file is newer than
    /// <paramref name="interval"/>.
    /// </summary>
    /// <param name="filePath">The database file to write.</param>
    /// <param name="apiKey">The IP2Location download token.</param>
    /// <param name="forIpRange">
    /// <see langword="true"/> to build the zip <see cref="LocalIpRangeLocationProvider"/> reads;
    /// <see langword="false"/> for the file <see cref="LocalIpLocationProvider.Deserialize"/> reads.
    /// </param>
    /// <param name="interval">How old the file may be before it is rebuilt, or <see langword="null"/> for 7 days.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="HttpRequestException">The download failed.</exception>
    /// <exception cref="Exception">The download has no IP2LOCATION-LITE-DB1.IPV6.CSV.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="FormatException">A row has an address that is not a number.</exception>
    public static async Task UpdateLocalDb(string filePath, string apiKey, bool forIpRange, TimeSpan? interval,
        CancellationToken cancellationToken)
    {
        interval ??= TimeSpan.FromDays(7);
        if (File.GetLastWriteTimeUtc(filePath) > DateTime.UtcNow - interval)
            return;

        // copy zip to memory
        // ReSharper disable once ShortLivedHttpClient
        using var httpClient = new HttpClient();
        // ReSharper disable once StringLiteralTypo
        var url = $"https://www.ip2location.com/download/?token={apiKey}&file=DB1LITECSVIPV6";
        await using var ipLocationZipNetStream = await httpClient.GetStreamAsync(url, cancellationToken).Vhc();
        using var ipLocationZipStream = new MemoryStream();
        await ipLocationZipNetStream.CopyToAsync(ipLocationZipStream, cancellationToken).Vhc();
        ipLocationZipStream.Position = 0;

        // build new ipLocation filePath
        await using var ipLocationZipArchive = new ZipArchive(ipLocationZipStream, ZipArchiveMode.Read);
        const string entryName = "IP2LOCATION-LITE-DB1.IPV6.CSV";
        var ipLocationEntry = ipLocationZipArchive.GetEntry(entryName) ??
                              throw new Exception($"{entryName} not found in the zip file!");

        await using var csvStream = await ipLocationEntry.OpenAsync(cancellationToken).Vhc();
        if (forIpRange)
            await BuildLocalIpRangeLocationDb(csvStream, filePath, cancellationToken).Vhc();
        else
            await BuildLocalIpLocationDb(csvStream, filePath, cancellationToken).Vhc();
    }

    /// <summary>
    /// Builds the zip <see cref="LocalIpRangeLocationProvider"/> reads, with an entry of IP ranges per country
    /// (<c>cc.ips</c>), stamped so an app can tell the build.
    /// </summary>
    /// <param name="csvStream">IP2Location's CSV; it is closed after.</param>
    /// <param name="outputZipFile">The zip file to write; an existing one is overwritten.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="FormatException">A row has an address that is not a number.</exception>
    public static async Task BuildLocalIpRangeLocationDb(Stream csvStream, string outputZipFile,
        CancellationToken cancellationToken)
    {
        var countryIpRanges = await ParseIp2LocationCsv(csvStream, cancellationToken).Vhc();

        // Building the IpGroups directory structure
        VhLogger.Instance.LogDebug("Building the optimized Ip2Location archive...");
        // stamped (ZipStamp): what an app and IpLocationSqliteBuilder tell the build by, read from the
        // bytes in front of it
        await using var outputStream = File.Create(outputZipFile);
        await using var newArchive = new StampedZipArchive(outputStream);
        foreach (var countryIpRange in countryIpRanges.OrderBy(x => x.Key)) {
            var ipRanges = new IpRangeOrderedList(countryIpRange.Value);
            var entry = newArchive.CreateEntry($"{countryIpRange.Key}.ips", CompressionLevel.NoCompression);
            await using var entryStream = await entry.OpenAsync(cancellationToken).Vhc();
            ipRanges.Serialize(entryStream);
        }
    }

    /// <summary>
    /// Builds the file <see cref="LocalIpLocationProvider.Deserialize"/> reads.
    /// </summary>
    /// <param name="csvStream">IP2Location's CSV; it is closed after.</param>
    /// <param name="outputFile">The file to write; an existing one is overwritten.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="FormatException">A row has an address that is not a number.</exception>
    public static async Task BuildLocalIpLocationDb(Stream csvStream, string outputFile,
        CancellationToken cancellationToken)
    {
        var countries = await ParseIp2LocationCsv(csvStream, cancellationToken).Vhc();

        // Building the IpGroups directory structure
        VhLogger.Instance.LogDebug("Building the optimized Ip2Location archive...");
        var ipRangeInfos = new List<LocalIpLocationProvider.IpRangeInfo>();
        foreach (var country in countries) {
            cancellationToken.ThrowIfCancellationRequested();
            ipRangeInfos.AddRange(
                country.Value.Select(ipRange => new LocalIpLocationProvider.IpRangeInfo {
                    CountryCode = country.Key,
                    IpRange = ipRange
                }));
        }

        // write to file
        await using var outputStream = File.Create(outputFile);
        var ipRangeLocationProvider = new LocalIpLocationProvider(ipRangeInfos);
        ipRangeLocationProvider.Serialize(outputStream);
    }

    /// <summary>
    /// Reads IP2Location's CSV into each country's IP ranges.
    /// </summary>
    /// <remarks>
    /// Rows with no country (<c>-</c>) and rows without four columns are skipped, a quoted column keeping its commas;
    /// <c>um</c>, the US Minor Outlying Islands, counts as <c>us</c>; IPv4-mapped IPv6 ranges become IPv4 ones.
    /// </remarks>
    /// <param name="ipLocationsCsvStream">IP2Location's CSV; it is closed after.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result maps each country's code, in lower case, to its ranges.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="FormatException">A row has an address that is not a number.</exception>
    public static async Task<Dictionary<string, List<IpRange>>> ParseIp2LocationCsv(Stream ipLocationsCsvStream,
        CancellationToken cancellationToken)
    {
        // extract IpGroups
        var ipGroupIpRanges = new Dictionary<string, List<IpRange>>();
        using var streamReader = new StreamReader(ipLocationsCsvStream);
        while (!streamReader.EndOfStream) {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await streamReader.ReadLineAsync(cancellationToken).Vhc() ?? "";
            var items = SplitCsvLine(line);
            if (items.Count != 4)
                continue;

            var ipGroupId = items[2].ToLowerInvariant();
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

    // A CSV line's fields (RFC 4180): a quoted field keeps its commas, as in "Tanzania, United Republic of", and a
    // doubled quote inside it is one quote. Split at every comma, such a row has a field too many and was left out,
    // with its country's ranges.
    private static List<string> SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var isQuoted = false;
        for (var i = 0; i < line.Length; i++) {
            var ch = line[i];
            if (isQuoted && ch == '"' && i + 1 < line.Length && line[i + 1] == '"') {
                field.Append('"');
                i++;
            }
            else if (ch == '"') {
                isQuoted = !isQuoted;
            }
            else if (ch == ',' && !isQuoted) {
                fields.Add(field.ToString());
                field.Clear();
            }
            else {
                field.Append(ch);
            }
        }

        fields.Add(field.ToString());
        return fields;
    }
}