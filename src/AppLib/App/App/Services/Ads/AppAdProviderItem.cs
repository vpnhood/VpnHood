using VpnHood.AppLib.Abstractions.Ads;

namespace VpnHood.AppLib.Services.Ads;

public class AppAdProviderItem
{
    public string Name => ProviderName ?? AdProvider.NetworkName;
    public string? ProviderName { get; init; }
    public required IAdProvider AdProvider { get; init; }
    public bool CanShowOverVpn { get; init; }
    public string[] IncludeCountryCodes { get; init; } = [];
    public string[] ExcludeCountryCodes { get; init; } = [];
    public bool IsEnabled { get; set; } = true;
    public bool IsFallback { get; set; }
}