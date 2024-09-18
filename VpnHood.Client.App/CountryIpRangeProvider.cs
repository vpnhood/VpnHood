using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Logging;
using VpnHood.Common.Net;
using VpnHood.Common.Utils;

namespace VpnHood.Client.App;

public class CountryIpRangeProvider
{
    private readonly ZipArchive _zipArchive;
    private string[]? _countryCodes;
    private readonly Dictionary<string, IpRangeOrderedList> _countryIpRanges = new();

    private CountryIpRangeProvider(ZipArchive zipArchive)
    {
        _zipArchive = zipArchive;
    }

    public static Task<CountryIpRangeProvider> Create(ZipArchive zipArchive)
    {
        var ret = new CountryIpRangeProvider(zipArchive);
        return Task.FromResult(ret);
    }

    public Task<string[]> GetCountryCodes()
    {
        _countryCodes ??= _zipArchive.Entries
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
                _zipArchive
                    .GetEntry($"{countryCode.ToLower()}.ips")?
                    .Open() ?? throw new NotExistsException();

            return IpRangeOrderedList.Deserialize(stream);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not load ip ranges. CountryCode: {CountryCode}", countryCode);
            return IpRangeOrderedList.Empty;
        }
    }

    public async Task<CountryIpRange> GetCountryIpRange(IPAddress ipAddress, string? lastCountryCode)
    {
        return await FindCountryIpRange(ipAddress, lastCountryCode).VhConfigureAwait()
               ?? throw new NotExistsException(
                   $"Could not find any ip group for the given ip. IP: {VhLogger.Format(ipAddress)}");
    }

    public async Task<CountryIpRange?> FindCountryIpRange(IPAddress ipAddress, string? lastCountryCode)
    {
        // CountryIpRange
        if (lastCountryCode != null) {
            var ipRanges = await GetIpRanges(lastCountryCode).VhConfigureAwait();
            if (ipRanges.Any(x => x.IsInRange(ipAddress))) {
                _countryIpRanges.TryAdd(lastCountryCode, ipRanges);
                return new CountryIpRange {
                    CountryCode = lastCountryCode,
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

    public async Task<string?> GetCountryCodeByCurrentIp()
    {
        try {
            var ipAddress =
                await IPAddressUtil.GetPublicIpAddress(AddressFamily.InterNetwork).VhConfigureAwait() ??
                await IPAddressUtil.GetPublicIpAddress(AddressFamily.InterNetworkV6).VhConfigureAwait();

            if (ipAddress == null)
                return null;

            var countryIpRange = await FindCountryIpRange(ipAddress, null).VhConfigureAwait();
            return countryIpRange?.CountryCode;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not retrieve client country from public ip services.");
            return null;
        }
    }
}