using VpnHood.AppLib.Api.Settings;

namespace VpnHood.AppLib.Api.SplitTunneling;

/// <summary>
/// Represents the split tunneling in force, as the settings and the session make it, so the UI holds no rule of its own.
/// </summary>
/// <remarks>
/// It shows the settings as the app resolves them, at once: a change that waits for a reconnect
/// (<see cref="VpnHood.AppLib.Api.App.AppState.IsReconnectRequired"/>) shows before it applies. With the split tunneling
/// off, every flag but <see cref="IsLocalNetworkSplit"/> and <see cref="IsAppSplit"/> is <see langword="false"/>; the
/// modes are the ones in force, while the stored ones stay in the settings.
/// </remarks>
public class SplitTunnelingState
{
    /// <summary>Gets a value indicating whether the split tunneling is on, as the settings' switch says.</summary>
    public required bool IsEnabled { get; init; }

    /// <summary>
    /// Gets a value indicating whether some public traffic of the tunneled apps may go outside the tunnel: by country, IP
    /// range or domain, by the server's declarations, or IPv6. Neither the local network, which cannot show the public
    /// address, nor the split by app counts.
    /// </summary>
    public required bool IsSplittingTraffic { get; init; }

    /// <summary>Gets a value indicating whether the split by app is in force; it stays with the split tunneling off.</summary>
    public required bool IsAppSplit { get; init; }

    /// <summary>Gets a value indicating whether the split by country is in force.</summary>
    public required bool IsCountrySplit { get; init; }

    /// <summary>Gets a value indicating whether the app's own split by IP ranges is in force.</summary>
    public required bool IsIpViaAppSplit { get; init; }

    /// <summary>Gets a value indicating whether the device adapter's split by IP ranges is in force.</summary>
    public required bool IsIpViaDeviceSplit { get; init; }

    /// <summary>Gets a value indicating whether the split by domain is in force.</summary>
    public required bool IsDomainSplit { get; init; }

    /// <summary>Gets a value indicating whether the local network goes outside the tunnel.</summary>
    public required bool IsLocalNetworkSplit { get; init; }

    /// <summary>Gets a value indicating whether IPv6 goes outside the tunnel: the server cannot carry it, and the setting excludes rather than blocks it.</summary>
    public required bool IsIpV6Split { get; init; }

    /// <summary>Gets a value indicating whether the server's own declarations leave public destinations out of the tunnel, while splitting is allowed.</summary>
    public required bool IsSplitByServer { get; init; }

    /// <summary>Gets the split by country's mode in force.</summary>
    public required SplitCountryMode CountryMode { get; init; }

    /// <summary>Gets the countries the split by country names, by their codes.</summary>
    public required IReadOnlyList<string> Countries { get; init; }

    /// <summary>Gets the DNS's split mode in force.</summary>
    public required SplitDnsMode DnsMode { get; init; }

    /// <summary>Gets what happens to IPv6 the server cannot carry, as in force.</summary>
    public required SplitUnsupportedIpMode UnsupportedIpV6Mode { get; init; }
}
