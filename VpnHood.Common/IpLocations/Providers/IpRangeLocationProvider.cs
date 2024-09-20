using System.Globalization;
using System.Net;
using System.Net.Sockets;
using VpnHood.Common.Net;
using VpnHood.Common.Utils;

namespace VpnHood.Common.IpLocations.Providers;

public class IpRangeLocationProvider : IIpLocationProvider
{
    public class IpRangeInfo
    {
        public required IpRange IpRanges { get; init; }
        public required string CountryCode { get; init; }
    }

    private readonly List<IpRangeInfo> _ipRangeInfoList;

    public IpRangeLocationProvider(IEnumerable<IpRangeInfo> ipRangeInfos)
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
            if (IPAddressUtil.Compare(x.IpRanges.FirstIpAddress, x.IpRanges.FirstIpAddress) <= 0 &&
                IPAddressUtil.Compare(x.IpRanges.LastIpAddress, x.IpRanges.LastIpAddress) >= 0)
                return 0;

            if (IPAddressUtil.Compare(x.IpRanges.FirstIpAddress, x.IpRanges.FirstIpAddress) < 0)
                return -1;

            return +1;
        }
    }
}