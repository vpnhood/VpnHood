
using VpnHood.AppLib.Api.SplitTunneling;

namespace VpnHood.AppLib.Api.Settings;

/// <summary>Represents the split tunneling's settings, as the person stored them.</summary>
public class SplitTunnelingSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether splitting is allowed at all. The default is <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// Off keeps the traffic of the tunneled apps from going outside the tunnel: every split turns inert, and both modes
    /// for unsupported addresses act as <see cref="SplitUnsupportedIpMode.Block"/>, whatever they store. The local network
    /// (<see cref="UseLocalNetwork"/>) and the split by app stay, as neither can show the public address of the traffic the
    /// app protects. While the app waits on an ad, though, traffic other than DNS goes outside the tunnel all the same.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the split by app's mode. The default is <see cref="SplitAppMode.All"/>.</summary>
    public SplitAppMode AppMode { get; set; } = SplitAppMode.All;

    /// <summary>Gets or sets the apps the split by app names, by their IDs. The default is none.</summary>
    public IReadOnlyList<string> Apps { get; set; } = [];

    /// <summary>Gets or sets the split by country's mode. The default is <see cref="SplitCountryMode.IncludeAll"/>.</summary>
    public SplitCountryMode CountryMode { get; set; } = SplitCountryMode.IncludeAll;

    /// <summary>Gets or sets the countries the split by country names, by their codes. The default is none.</summary>
    public IReadOnlyList<string> Countries { get; set; } = [];

    /// <summary>Gets or sets a value indicating whether the app's own split by IP ranges is on.</summary>
    public bool UseIpViaApp { get; set; }

    /// <summary>Gets or sets a value indicating whether the device adapter's split by IP ranges is on.</summary>
    public bool UseIpViaDevice { get; set; }

    /// <summary>Gets or sets a value indicating whether the split by domain is on.</summary>
    public bool UseDomain { get; set; }

    /// <summary>Gets or sets a value indicating whether the local network goes outside the tunnel.</summary>
    public bool UseLocalNetwork { get; set; }

    /// <summary>Gets or sets the DNS's split mode. The default is <see cref="SplitDnsMode.IncludeAll"/>.</summary>
    public SplitDnsMode DnsMode { get; set; } = SplitDnsMode.IncludeAll;

    /// <summary>
    /// Gets or sets what happens to a destination the server does not route: <see cref="SplitUnsupportedIpMode.Exclude"/>
    /// bypasses it, <see cref="SplitUnsupportedIpMode.Block"/> fails closed. The default is
    /// <see cref="SplitUnsupportedIpMode.Exclude"/>.
    /// </summary>
    /// <remarks>The UI does not show it; its block covers IPv6 too, over <see cref="UnsupportedIpV6Mode"/>.</remarks>
    public SplitUnsupportedIpMode UnroutedIpMode { get; set; } = SplitUnsupportedIpMode.Exclude;

    /// <summary>
    /// Gets or sets what happens to IPv6 where the server cannot carry it: <see cref="SplitUnsupportedIpMode.Block"/> keeps
    /// it inside, dead but private, and <see cref="SplitUnsupportedIpMode.Exclude"/> lets the system route it, working but
    /// visible to WebRTC and STUN probes. The default is <see cref="SplitUnsupportedIpMode.Block"/>.
    /// </summary>
    public SplitUnsupportedIpMode UnsupportedIpV6Mode { get; set; } = SplitUnsupportedIpMode.Block;
}
