using VpnHood.AppLib.Abstractions.Ads;

namespace VpnHood.AppLib.App.Services.Ads;

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
    /// Gets a value indicating whether the network can show its ads while the VPN is connected. Nothing in the app acts
    /// on it.
    /// </summary>
    public bool CanShowOverVpn { get; init; }

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
    /// Gets or sets a value indicating whether the network is tried only where its caller asks for the fallbacks too.
    /// The default is <see langword="false"/>.
    /// </summary>
    public bool IsFallback { get; set; }
}