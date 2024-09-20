using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using VpnHood.Common.Net;
using VpnHood.Common.Utils;

namespace VpnHood.Common.IpLocations.Providers;

public class LocalIpLocationProvider : IIpLocationProvider
{
    public class IpRangeInfo
    {
        public required IpRange IpRanges { get; init; }
        public required string CountryCode { get; init; }
    }

    private readonly List<IpRangeInfo> _ipRangeInfoList;

    public LocalIpLocationProvider(IEnumerable<IpRangeInfo> ipRangeInfos)
    {
        _ipRangeInfoList = ipRangeInfos
            .OrderBy(x => x.IpRanges.FirstIpAddress, new IPAddressComparer())
            .ToList();
    }

    public async Task<IpLocation> GetCurrentLocation(CancellationToken cancellationToken)
    {
        var ipAddress =
            await IPAddressUtil.GetPublicIpAddress(AddressFamily.InterNetwork, cancellationToken).VhConfigureAwait() ??
            await IPAddressUtil.GetPublicIpAddress(AddressFamily.InterNetworkV6, cancellationToken).VhConfigureAwait() ??
            throw new Exception("Could not find any public ip address.");

        var ipLocation = await GetLocation(ipAddress, cancellationToken);
        return ipLocation;
    }

    public Task<IpLocation> GetLocation(IPAddress ipAddress, CancellationToken cancellationToken)
    {
        if (ipAddress.IsIPv4MappedToIPv6)
            ipAddress = ipAddress.MapToIPv4();

        // binary search
        var itemToSearch = new IpRangeInfo {
            CountryCode = "?",
            IpRanges = new IpRange(ipAddress, ipAddress)
        };
        var res = _ipRangeInfoList.BinarySearch(itemToSearch, new IpRangeSearchComparer());
        if (res < 0)
            throw new KeyNotFoundException($"Could not find the location of the ip address. IpAddress: {ipAddress}");

        // return country code
        var countryCode = _ipRangeInfoList[res].CountryCode;
        var ipLocation = new IpLocation {
            CountryName = new RegionInfo(countryCode).EnglishName,
            CountryCode = countryCode,
            IpAddress = ipAddress,
            RegionName = null,
            CityName = null,
        };

        return Task.FromResult(ipLocation);
    }

    private class IpRangeSearchComparer : IComparer<IpRangeInfo>
    {
        public int Compare(IpRangeInfo x, IpRangeInfo y)
        {
            if (IPAddressUtil.Compare(x.IpRanges.FirstIpAddress, y.IpRanges.FirstIpAddress) <= 0 &&
                IPAddressUtil.Compare(x.IpRanges.LastIpAddress, y.IpRanges.LastIpAddress) >= 0)
                return 0;

            if (IPAddressUtil.Compare(x.IpRanges.FirstIpAddress, y.IpRanges.FirstIpAddress) < 0)
                return -1;

            return +1;
        }
    }

    public void Serialize(Stream stream)
    {
        // serialize to binary
        using var writer = new BinaryWriter(stream, Encoding.ASCII, true);
        writer.Write(_ipRangeInfoList.Count);
        foreach (var ipRangeInfo in _ipRangeInfoList) {
            var ipRange = ipRangeInfo.IpRanges;
            var firstIpBytes = ipRange.FirstIpAddress.GetAddressBytes();
            var lastIpBytes = ipRange.LastIpAddress.GetAddressBytes();
            var countryBytes = Encoding.ASCII.GetBytes(ipRangeInfo.CountryCode);

            writer.Write((byte)firstIpBytes.Length);
            writer.Write(firstIpBytes);
            writer.Write((byte)lastIpBytes.Length);
            writer.Write(lastIpBytes);
            writer.Write((byte)countryBytes.Length);
            writer.Write(countryBytes);
        }
    }

    public static LocalIpLocationProvider Deserialize(Stream stream)
    {
        using var reader = new BinaryReader(stream);
        var length = reader.ReadInt32();
        var ipRangeInfos = new IpRangeInfo[length];
        for (var i = 0; i < length; i++) {
            var firstIpLength = reader.ReadByte();
            var firstIpBytes = reader.ReadBytes(firstIpLength);
            var lastIpLength = reader.ReadByte();
            var lastIpBytes = reader.ReadBytes(lastIpLength);
            var countryLength = reader.ReadByte();
            var countryBytes = reader.ReadBytes(countryLength);

            ipRangeInfos[i] = new IpRangeInfo {
                CountryCode = Encoding.ASCII.GetString(countryBytes),
                IpRanges = new IpRange(new IPAddress(firstIpBytes), new IPAddress(lastIpBytes))
            };
        }

        return new LocalIpLocationProvider(ipRangeInfos);
    }

}