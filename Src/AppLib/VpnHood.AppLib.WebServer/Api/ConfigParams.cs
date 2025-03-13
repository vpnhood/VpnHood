using VpnHood.AppLib.Abstractions;
namespace VpnHood.AppLib.WebServer.Api;

public class ConfigParams
{
    public string[] AvailableCultures { get; init; } = [];
    public AppResources.AppStrings? Strings { get; init; }
}