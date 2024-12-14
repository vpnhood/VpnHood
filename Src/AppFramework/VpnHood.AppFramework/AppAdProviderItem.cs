using VpnHood.AppFramework.Abstractions;

namespace VpnHood.AppFramework;

public class AppAdProviderItem
{
    public string Name => ProviderName ?? AdProvider.NetworkName;
    public string? ProviderName { get; init; }
    public required IAppAdProvider AdProvider { get; init; }
    public string[] IncludeCountryCodes { get; init; } = [];
    public string[] ExcludeCountryCodes { get; init; } = [];
}