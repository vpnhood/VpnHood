using VpnHood.Client.App.Abstractions;

namespace VpnHood.Client.App.Droid.Common.Activities;

public class AndroidMainActivityOptions
{
    public string[] AccessKeySchemes { get; init; } = [];
    public string[] AccessKeyMimes { get; init; } = [];
    public IAppUpdaterService? AppUpdaterService { get; init; }
    public bool RequestFeaturesOnCreate { get; init; } = true;
}