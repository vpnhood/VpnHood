using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Logging;
using VpnHood.Common.Net;
using VpnHood.Common.Utils;

namespace VpnHood.Common.IpLocations.Providers;

public class CountryIpRangeProvider(
    Func<ZipArchive> zipArchiveFactory,
    Func<string?> currentCountryCodeFunc) 
    : IIpLocationProvider
{
    private string? _lasCurrentCountryCode;
    private string[]? _countryCodes;
    private readonly Dictionary<string, IpRangeOrderedList> _countryIpRanges = new();
    private readonly Lazy<ZipArchive> _zipArchive = new(zipArchiveFactory);
    private string? CurrentCountryCode => currentCountryCodeFunc() ?? _lasCurrentCountryCode;

    public Task<string[]> GetCountryCodes()
    {
        _countryCodes ??= _zipArchive.Value.Entries
            .Where(x => Path.GetExtension(x.Name) == ".ips")
            .Select(x => Path.GetFileNameWithoutExtension(x.Name))
            .ToArray();

        return Task.FromResult(_countryCodes);
    }

    public async Task<IpRangeOrderedList> GetIpRanges(string countryCode)
    {
        var ipRanges = await GetIpRangesInternal(countryCode).VhConfigureAwait();
        _countryIpRanges.TryAdd(countryCode, ipRanges);
        return ipRanges;
    }

    private async Task<IpRangeOrderedList> GetIpRangesInternal(string countryCode)
    {
        if (_countryIpRanges.TryGetValue(countryCode, out var countryIpRangeCache))
            return countryIpRangeCache;

        try {
            await using var stream =
                _zipArchive.Value
                    .GetEntry($"{countryCode.ToLower()}.ips")?
                    .Open() ?? throw new NotExistsException();

            return IpRangeOrderedList.Deserialize(stream);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not load ip ranges. CountryCode: {CountryCode}", countryCode);
            return IpRangeOrderedList.Empty;
        }
    }

    public async Task<CountryIpRange> GetCountryIpRange(IPAddress ipAddress)
    {
        return await FindCountryIpRange(ipAddress).VhConfigureAwait()
               ?? throw new NotExistsException(
                   $"Could not find any ip group for the given ip. IP: {VhLogger.Format(ipAddress)}");
    }

    public async Task<CountryIpRange?> FindCountryIpRange(IPAddress ipAddress)
    {
        // CountryIpRange
        if (CurrentCountryCode != null) {
            var ipRanges = await GetIpRanges(CurrentCountryCode).VhConfigureAwait();
            if (ipRanges.Any(x => x.IsInRange(ipAddress))) {
                _countryIpRanges.TryAdd(CurrentCountryCode, ipRanges);
                return new CountryIpRange {
                    CountryCode = CurrentCountryCode,
                    IpRanges = ipRanges
                };
            }
        }

        // iterate through all groups
        var countryCodes = await GetCountryCodes();
        foreach (var countryCode in countryCodes) {
            var ipRanges = await GetIpRanges(countryCode).VhConfigureAwait();
            if (ipRanges.Any(x => x.IsInRange(ipAddress))) {
                _countryIpRanges.TryAdd(countryCode, ipRanges);
                return new CountryIpRange {
                    CountryCode = countryCode,
                    IpRanges = ipRanges
                };
            }
        }

        return null;
    }

    public async Task<IpLocation> GetCurrentLocation(CancellationToken cancellationToken)
    {
        var ipAddress =
            await IPAddressUtil.GetPublicIpAddress(AddressFamily.InterNetwork, cancellationToken).VhConfigureAwait() ??
            await IPAddressUtil.GetPublicIpAddress(AddressFamily.InterNetworkV6, cancellationToken).VhConfigureAwait() ??
            throw new Exception("Could not find any public ip address.");

        var ipLocation = await GetLocation(ipAddress, cancellationToken);
        _lasCurrentCountryCode = ipLocation.CountryCode;
        return ipLocation;
    }

    public async Task<IpLocation> GetLocation(IPAddress ipAddress, CancellationToken cancellationToken)
    {
        var countryIpRange = await GetCountryIpRange(ipAddress).VhConfigureAwait();
        return new IpLocation {
            CountryName = new RegionInfo(countryIpRange.CountryCode).EnglishName,
            CountryCode = countryIpRange.CountryCode,
            IpAddress = ipAddress,
            CityName = null,
            RegionName = null
        };
    }

    public class CountryIpRange
    {
        public required string CountryCode { get; init; }
        public required IpRangeOrderedList IpRanges { get; init; }
    }
}