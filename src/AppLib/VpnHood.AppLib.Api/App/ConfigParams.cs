using VpnHood.AppLib.Api.App;

namespace VpnHood.AppLib.Api.App;

public class ConfigParams
{
    public string[] AvailableCultures { get; init; } = [];
    public AppStrings? Strings { get; init; }
}