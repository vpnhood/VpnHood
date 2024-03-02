using VpnHood.Client.App.Abstractions;

namespace VpnHood.Client.App.Droid.Common.Activities;

public class AndroidMainActivityOptions
{
    public string[] AccessKeySchemes { get; init; } = Array.Empty<string>();
    public string[] AccessKeyMimes { get; init; } = Array.Empty<string>();
    public IAppUpdaterService? AppUpdaterService { get; init; }
    public bool RequestFeaturesOnCreate { get; init; } = true;
}