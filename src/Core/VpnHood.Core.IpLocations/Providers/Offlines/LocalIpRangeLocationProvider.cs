using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Assets;
using VpnHood.Core.Toolkit.Exceptions;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Streams;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.IpLocations.Providers.Offlines;

// Every country's IP ranges, out of one zip the caller names: an entry per country ({code}.ips),
// read on the first lookup that needs it and kept open for the rest, since a lookup walks many
// countries and each is a few hundred KB.
public class LocalIpRangeLocationProvider(
    IAsset zipAsset,
    Func<string?> currentCountryCodeFunc)
    : IIpRangeLocationProvider
{
    private readonly AsyncLock _lock = new();
    private string? _lasCurrentCountryCode;
    private string[]? _countryCodes;
    private readonly Dictionary<string, IpRangeOrderedList> _countryIpRanges = new();
    private ZipArchive? _zipArchive;
    private string? CurrentCountryCode => currentCountryCodeFunc() ?? _lasCurrentCountryCode;

    public async Task<string[]> GetCountryCodes(CancellationToken cancellationToken)
    {
        using var _ = await _lock.LockAsync(cancellationToken);
        if (_countryCodes != null)
            return _countryCodes;

        var zipArchive = await GetZipArchive(cancellationToken).Vhc();
        _countryCodes = zipArchive.Entries
            .Where(x => Path.GetExtension(x.Name) == ".ips")
            .Select(x => Path.GetFileNameWithoutExtension(x.Name).ToUpper())
            .ToArray();

        return _countryCodes;
    }

    public async Task<IpRangeOrderedList> GetIpRanges(string countryCode, CancellationToken cancellationToken)
    {
        using var _ = await _lock.LockAsync(cancellationToken);
        var ipRanges = await GetIpRangesInternal(countryCode, cancellationToken).Vhc();
        _countryIpRanges.TryAdd(countryCode, ipRanges);
        return ipRanges;
    }

    // must be called within async lock
    private async Task<IpRangeOrderedList> GetIpRangesInternal(string countryCode, CancellationToken cancellationToken)
    {
        if (_countryIpRanges.TryGetValue(countryCode, out var countryIpRangeCache))
            return countryIpRangeCache;

        try {
            var zipArchive = await GetZipArchive(cancellationToken).Vhc();
            var entry = zipArchive.GetEntry($"{countryCode.ToLower()}.ips")
                        ?? throw new NotExistsException();
            await using var stream = await entry.OpenAsync(cancellationToken);
            return IpRangeOrderedList.Deserialize(stream);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not load ip ranges. CountryCode: {CountryCode}", countryCode);
            return IpRangeOrderedList.Empty;
        }
    }

    public async Task<IpLocation> GetCurrentLocation(CancellationToken cancellationToken)
    {
        var ipAddress =
            await IPAddressUtil.GetPublicIpAddress(AddressFamily.InterNetwork, cancellationToken).Vhc()
            ?? await IPAddressUtil.GetPublicIpAddress(AddressFamily.InterNetworkV6, cancellationToken).Vhc()
            ?? throw new Exception("Could not find any public ip address.");

        var ipLocation = await GetLocation(ipAddress, cancellationToken);
        _lasCurrentCountryCode = ipLocation.CountryCode;
        return ipLocation;
    }

    public async Task<IpLocation> GetLocation(IPAddress ipAddress, CancellationToken cancellationToken)
    {
        // first try CurrentCountryCode for performance
        if (CurrentCountryCode != null) {
            var ipRanges = await GetIpRanges(CurrentCountryCode, cancellationToken).Vhc();
            if (ipRanges.Any(x => x.IsInRange(ipAddress)))
                return BuildIpLocation(CurrentCountryCode, ipAddress);
        }

        // iterate through all countries
        var countryCodes = await GetCountryCodes(cancellationToken);
        foreach (var countryCode in countryCodes) {
            var ipRanges = await GetIpRanges(countryCode, cancellationToken).Vhc();
            if (ipRanges.Any(x => x.IsInRange(ipAddress)))
                return BuildIpLocation(countryCode, ipAddress);
        }

        throw new KeyNotFoundException($"Could not find location for given ip. IpAddress: {ipAddress}.");
    }

    private static IpLocation BuildIpLocation(string countryCode, IPAddress ipAddress)
    {
        countryCode = countryCode.ToUpper();
        return new IpLocation {
            CountryName = new RegionInfo(countryCode).EnglishName,
            CountryCode = countryCode,
            IpAddress = ipAddress,
            CityName = null,
            RegionName = null
        };
    }

    // Opened on the first read that needs it, within the lock, and kept for the rest.
    private async Task<ZipArchive> GetZipArchive(CancellationToken cancellationToken)
    {
        if (_zipArchive != null)
            return _zipArchive;

        // An archive reads its directory from the end, and what a package hands out on Android only
        // goes forward, so there it is a copy in memory - held for as long as the archive is, which
        // is the life of this provider: it is worth knowing that this is where the database sits.
        var stream = await zipAsset.OpenReadAsync(cancellationToken).Vhc();
        var seekable = await stream.ToMemoryStreamIfNotSeekableAsync(cancellationToken).Vhc();

        // the archive owns the stream from here and closes it with itself
        _zipArchive = new ZipArchive(seekable, ZipArchiveMode.Read);
        return _zipArchive;
    }

    public void Dispose()
    {
        _zipArchive?.Dispose();
    }
}