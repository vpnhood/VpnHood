using VpnHood.AppLogic.Abstractions.Ads;

namespace VpnHood.AppLogic.App.Services.Ads;

/// <summary>Represents an ad network the app shows ads from (<see cref="AppOptions.AdProviderItems"/>).</summary>
public class AppAdProviderItem
{
    /// <summary>
    /// Gets the item's name: <see cref="ProviderName"/>, or else the network's own
    /// (<see cref="IAdProvider.NetworkName"/>).
    /// </summary>
    public string Name => ProviderName ?? AdProvider.NetworkName;

    /// <summary>Gets the name the item goes by, or <see langword="null"/> for the network's own.</summary>
    public string? ProviderName { get; init; }

    /// <summary>Gets the network's provider.</summary>
    public required IAdProvider AdProvider { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether the network can show its ads while the VPN is connected, their traffic
    /// going through it. An ad is tried before the VPN starts, then around the tunnel, then through it where a network
    /// for that kind of ad can where the person is (<see cref="VpnHood.Core.Client.Abstractions.AdRoute"/>): only those
    /// networks load that last try, the house ad too (<see cref="IsFallback"/>). The app's remote settings
    /// (<see cref="AppOptions.RemoteSettingsUrl"/>) set it at the app's start where they list the networks that can, by
    /// name, as <c>"OverVpnAdProviders": ["name"]</c>. The default is <see langword="false"/>, for networks that refuse
    /// VPN traffic.
    /// </summary>
    public bool CanShowOverVpn { get; set; }

    /// <summary>
    /// Gets the only countries the network's ads are loaded in, by their codes, or empty for every country. An item may
    /// not have both this and <see cref="ExcludeCountryCodes"/>; the app's start fails on one that does.
    /// </summary>
    public string[] IncludeCountryCodes { get; init; } = [];

    /// <summary>
    /// Gets the countries the network's ads are not loaded in, by their codes, or empty for none.
    /// </summary>
    public string[] ExcludeCountryCodes { get; init; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether the app loads ads from the network. The default is
    /// <see langword="true"/>.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the network is a fallback, as a house ad that always shows: it is tried
    /// only on an ad's last route (<see cref="VpnHood.Core.Client.Abstractions.AdRoute"/>), after the other networks
    /// wherever the head lists it, so a paid ad gets every try first; through the tunnel only where it is marked, as any
    /// network (<see cref="CanShowOverVpn"/>). The default is <see langword="false"/>.
    /// </summary>
    public bool IsFallback { get; set; }

    // the item's own name or its network's, as the remote settings name it
    internal bool HasName(string name) =>
        string.Equals(ProviderName, name, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(AdProvider.NetworkName, name, StringComparison.OrdinalIgnoreCase);
}