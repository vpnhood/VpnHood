using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Net.IpLocations.Providers.Offlines;

/// <summary>
/// Provides locations, the country only, from IP ranges held in memory, which <see cref="Deserialize"/> reads and
/// <see cref="Ip2LocationDbParser.BuildLocalIpLocationDb"/> builds.
/// </summary>
public class LocalIpLocationProvider : IIpLocationProvider
{
    /// <summary>Represents one IP range and its country.</summary>
    public class IpRangeInfo
    {
        /// <summary>Gets the IP range.</summary>
        public required IpRange IpRanges { get; init; }

        /// <summary>Gets the country's two-letter code.</summary>
        public required string CountryCode { get; init; }
    }

    private readonly List<IpRangeInfo> _ipRangeInfoList;
    private readonly string? _userAgent;

    /// <summary>Initializes a new instance of the <see cref="LocalIpLocationProvider"/> class.</summary>
    /// <param name="ipRangeInfos">The IP ranges with their countries.</param>
    /// <param name="userAgent">
    /// The User-Agent header the current location's public-IP lookup sends, or <see langword="null"/>.
    /// </param>
    public LocalIpLocationProvider(IEnumerable<IpRangeInfo> ipRangeInfos, string? userAgent = null)
    {
        _ipRangeInfoList = ipRangeInfos
            .OrderBy(x => x.IpRanges.FirstIpAddress, new IPAddressComparer())
            .ToList();
        _userAgent = userAgent;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It finds the device's public IP address online, IPv4 first, then looks the address up locally.
    /// </remarks>
    /// <exception cref="Exception">No public IP address was found.</exception>
    /// <exception cref="KeyNotFoundException">No range holds the address.</exception>
    public async Task<IpLocation> GetCurrentLocation(CancellationToken cancellationToken)
    {
        var ipAddress =
            await IPAddressUtil.GetPublicIpAddress(AddressFamily.InterNetwork, _userAgent, cancellationToken).Vhc() ??
            await IPAddressUtil.GetPublicIpAddress(AddressFamily.InterNetworkV6, _userAgent, cancellationToken)
                .Vhc() ??
            throw new Exception("Could not find any public ip address.");

        var ipLocation = await GetLocation(ipAddress, cancellationToken).Vhc();
        return ipLocation;
    }

    /// <inheritdoc/>
    /// <remarks>An IPv4-mapped IPv6 address is looked up as IPv4.</remarks>
    /// <exception cref="KeyNotFoundException">No range holds the address.</exception>
    public Task<IpLocation> GetLocation(IPAddress ipAddress, CancellationToken cancellationToken)
    {
        if (ipAddress.IsIPv4MappedToIPv6)
            ipAddress = ipAddress.MapToIPv4();

        // binary search
        var itemToSearch = new IpRangeInfo {
            CountryCode = "?",
            IpRanges = new IpRange(ipAddress)
        };
        var res = _ipRangeInfoList.BinarySearch(itemToSearch, new IpRangeSearchComparer());
        if (res < 0)
            throw new KeyNotFoundException($"Could not find the location of the ip address. IpAddress: {ipAddress}");

        // return country code
        var countryCode = _ipRangeInfoList[res].CountryCode.ToUpperInvariant();
        var ipLocation = new IpLocation {
            CountryName = new RegionInfo(countryCode).EnglishName,
            CountryCode = countryCode,
            IpAddress = ipAddress,
            RegionName = null,
            CityName = null
        };

        return Task.FromResult(ipLocation);
    }

    private class IpRangeSearchComparer : IComparer<IpRangeInfo>
    {
        public int Compare(IpRangeInfo? x, IpRangeInfo? y)
        {
            if (x is null && y is null) return 0;
            if (x is null) return -1; // null is less than any non-null value
            if (y is null) return +1; // any non-null value is greater than null

            if (IPAddressUtil.Compare(x.IpRanges.FirstIpAddress, y.IpRanges.FirstIpAddress) <= 0 &&
                IPAddressUtil.Compare(x.IpRanges.LastIpAddress, y.IpRanges.LastIpAddress) >= 0)
                return 0;

            if (IPAddressUtil.Compare(x.IpRanges.FirstIpAddress, y.IpRanges.FirstIpAddress) < 0)
                return -1;

            return +1;
        }
    }

    /// <summary>Writes the IP ranges in the binary form <see cref="Deserialize"/> reads.</summary>
    /// <param name="stream">The stream to write to; it stays open.</param>
    public void Serialize(Stream stream)
    {
        // serialize to binary
        using var writer = new BinaryWriter(stream, Encoding.ASCII, true);
        Span<byte> firstIpBuffer = stackalloc byte[16];
        Span<byte> lastIpBuffer = stackalloc byte[16];
        writer.Write(_ipRangeInfoList.Count);
        foreach (var ipRangeInfo in _ipRangeInfoList) {
            var ipRange = ipRangeInfo.IpRanges;
            var firstIpBytes = ipRange.FirstIpAddress.GetAddressBytesFast(firstIpBuffer);
            var lastIpBytes = ipRange.LastIpAddress.GetAddressBytesFast(lastIpBuffer);
            var countryBytes = Encoding.ASCII.GetBytes(ipRangeInfo.CountryCode);

            writer.Write((byte)firstIpBytes.Length);
            writer.Write(firstIpBytes);
            writer.Write((byte)lastIpBytes.Length);
            writer.Write(lastIpBytes);
            writer.Write((byte)countryBytes.Length);
            writer.Write(countryBytes);
        }
    }

    /// <summary>Reads IP ranges that <see cref="Serialize"/> wrote.</summary>
    /// <param name="stream">The stream to read from; it is closed after.</param>
    /// <param name="userAgent">
    /// The User-Agent header the current location's public-IP lookup sends, or <see langword="null"/>.
    /// </param>
    /// <returns>A new provider with the ranges.</returns>
    /// <exception cref="EndOfStreamException">The stream ends early.</exception>
    public static LocalIpLocationProvider Deserialize(Stream stream, string? userAgent = null)
    {
        using var reader = new BinaryReader(stream);
        var length = reader.ReadInt32();
        var ipRangeInfos = new IpRangeInfo[length];
        for (var i = 0; i < length; i++) {
            var firstIpBytes = ReadField(reader);
            var lastIpBytes = ReadField(reader);
            var countryBytes = ReadField(reader);

            ipRangeInfos[i] = new IpRangeInfo {
                CountryCode = Encoding.ASCII.GetString(countryBytes),
                IpRanges = new IpRange(new IPAddress(firstIpBytes), new IPAddress(lastIpBytes))
            };
        }

        return new LocalIpLocationProvider(ipRangeInfos, userAgent);
    }

    // A field with its length in front. All of it or an error: BinaryReader.ReadBytes gives what is left of a file
    // cut short, which would read as a shorter country code, or as an IPv4 address where 16 bytes were cut to 4.
    private static byte[] ReadField(BinaryReader reader)
    {
        var length = reader.ReadByte();
        var bytes = reader.ReadBytes(length);
        return bytes.Length == length
            ? bytes
            : throw new EndOfStreamException("The IP location data is cut short.");
    }

    /// <summary>Does nothing: the provider holds nothing to release.</summary>
    public void Dispose()
    {
    }
}