using VpnHood.Client.App.Abstractions;

namespace VpnHood.Client.App;

public class AppAdProviderItem
{
    public string Name => ProviderName ?? AdProvider.NetworkName;
    public string? ProviderName { get; init; }
    public required IAppAdProvider AdProvider { get; init; }
    public string[] IncludeCountryCodes { get; init; } = [];
    public string[] ExcludeCountryCodes { get; init; } = [];
}