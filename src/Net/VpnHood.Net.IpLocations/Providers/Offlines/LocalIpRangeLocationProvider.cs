using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Assets;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Streams;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Net.IpLocations.Providers.Offlines;

/// <summary>
/// Provides locations, the country only, and each country's IP ranges, from a zip with an entry of IP ranges per
/// country (<c>cc.ips</c>), which <see cref="Ip2LocationDbParser.BuildLocalIpRangeLocationDb"/> builds.
/// </summary>
/// <remarks>
/// A country's ranges are read on the first lookup that needs them and kept, since a lookup walks many countries. A lookup tries the current country first, then every country in turn. Where the asset's
/// stream cannot seek, as in an Android package, the zip is copied into memory for the provider's life.
/// </remarks>
/// <param name="zipAsset">The zip.</param>
/// <param name="currentCountryCodeFunc">
/// Returns the current country's code, tried first, or <see langword="null"/> to try the one
/// <see cref="GetCurrentLocation"/> found last.
/// </param>
/// <param name="userAgent">
/// The User-Agent header the current location's public-IP lookup sends, or <see langword="null"/>.
/// </param>
public class LocalIpRangeLocationProvider(
    IAsset zipAsset,
    Func<string?> currentCountryCodeFunc,
    string? userAgent = null)
    : IIpRangeLocationProvider
{
    private readonly AsyncLock _lock = new();
    private string? _lasCurrentCountryCode;
    private string[]? _countryCodes;
    private readonly Dictionary<string, IpRangeOrderedList> _countryIpRanges = new();
    private ZipArchive? _zipArchive;
    private string? CurrentCountryCode => currentCountryCodeFunc() ?? _lasCurrentCountryCode;

    /// <inheritdoc/>
    public async Task<string[]> GetCountryCodes(CancellationToken cancellationToken)
    {
        using var _ = await _lock.LockAsync(cancellationToken).Vhc();
        if (_countryCodes != null)
            return _countryCodes;

        var zipArchive = await GetZipArchive(cancellationToken).Vhc();
        _countryCodes = zipArchive.Entries
            .Where(x => Path.GetExtension(x.Name) == ".ips")
            .Select(x => Path.GetFileNameWithoutExtension(x.Name).ToUpperInvariant())
            .ToArray();

        return _countryCodes;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A country the zip has no entry for has no ranges. One whose entry cannot be read, or the zip itself, gives none
    /// for that call, and the next call reads it again. Either error is logged.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async Task<IpRangeOrderedList> GetIpRanges(string countryCode, CancellationToken cancellationToken)
    {
        using var _ = await _lock.LockAsync(cancellationToken).Vhc();
        if (_countryIpRanges.TryGetValue(countryCode, out var countryIpRangeCache))
            return countryIpRangeCache;

        try {
            var ipRanges = await ReadIpRanges(countryCode, cancellationToken).Vhc();
            _countryIpRanges.TryAdd(countryCode, ipRanges);
            return ipRanges;
        }
        // the caller's cancellation is no failed load
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch (Exception ex) {
            // not kept: the next call reads again, where a kept failure would leave the country without ranges for good
            VhLogger.Instance.LogError(ex, "Could not load ip ranges. CountryCode: {CountryCode}", countryCode);
            return IpRangeOrderedList.Empty;
        }
    }

    // Must be called within async lock. Whatever it returns is kept: a country the zip holds no file of has no
    // ranges, now or later, which only the zip's own list of entries tells; an asset that cannot be opened throws.
    private async Task<IpRangeOrderedList> ReadIpRanges(string countryCode, CancellationToken cancellationToken)
    {
        var zipArchive = await GetZipArchive(cancellationToken).Vhc();
        var entry = zipArchive.GetEntry($"{countryCode.ToLowerInvariant()}.ips");
        if (entry == null) {
            VhLogger.Instance.LogError("Could not load ip ranges: the zip has none for the country. CountryCode: {CountryCode}",
                countryCode);
            return IpRangeOrderedList.Empty;
        }

        await using var stream = await entry.OpenAsync(cancellationToken).Vhc();
        return IpRangeOrderedList.Deserialize(stream);
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
            await IPAddressUtil.GetPublicIpAddress(AddressFamily.InterNetwork, userAgent, cancellationToken).Vhc()
            ?? await IPAddressUtil.GetPublicIpAddress(AddressFamily.InterNetworkV6, userAgent, cancellationToken).Vhc()
            ?? throw new Exception("Could not find any public ip address.");

        var ipLocation = await GetLocation(ipAddress, cancellationToken).Vhc();
        _lasCurrentCountryCode = ipLocation.CountryCode;
        return ipLocation;
    }

    /// <inheritdoc/>
    /// <exception cref="KeyNotFoundException">No country's ranges hold the address.</exception>
    public async Task<IpLocation> GetLocation(IPAddress ipAddress, CancellationToken cancellationToken)
    {
        // first try CurrentCountryCode for performance
        if (CurrentCountryCode != null) {
            var ipRanges = await GetIpRanges(CurrentCountryCode, cancellationToken).Vhc();
            if (ipRanges.Any(x => x.IsInRange(ipAddress)))
                return BuildIpLocation(CurrentCountryCode, ipAddress);
        }

        // iterate through all countries
        var countryCodes = await GetCountryCodes(cancellationToken).Vhc();
        foreach (var countryCode in countryCodes) {
            var ipRanges = await GetIpRanges(countryCode, cancellationToken).Vhc();
            if (ipRanges.Any(x => x.IsInRange(ipAddress)))
                return BuildIpLocation(countryCode, ipAddress);
        }

        throw new KeyNotFoundException($"Could not find location for given ip. IpAddress: {ipAddress}.");
    }

    private static IpLocation BuildIpLocation(string countryCode, IPAddress ipAddress)
    {
        countryCode = countryCode.ToUpperInvariant();
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

    /// <summary>Closes the zip.</summary>
    public void Dispose()
    {
        _zipArchive?.Dispose();
    }
}