using VpnHood.Client.App.Abstractions;

namespace VpnHood.Client.App;

public class AppAdService
{
    public string Name => ServiceName ?? AdProvider.NetworkName;
    public string? ServiceName { get; init; }
    public required IAppAdProvider AdProvider { get; init; }
    public string[] IncludeCountryCodes { get; init; } = [];
    public string[] ExcludeCountryCodes { get; init; } = [];
}