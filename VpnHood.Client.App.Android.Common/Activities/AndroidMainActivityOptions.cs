using VpnHood.Client.App.Abstractions;

namespace VpnHood.Client.App.Droid.Common.Activities;

public class AndroidMainActivityOptions
{
    public string[] AccessKeySchemes { get; init; } = [];
    public string[] AccessKeyMimes { get; init; } = [];
    public bool CheckForUpdateOnCreate { get; init; } = true;
    public IAppUpdaterService? UpdaterService { get; init; }
    public IAppAccountService? AccountService { get; init; }
    public IAppAdService? AdService { get; init; }
}