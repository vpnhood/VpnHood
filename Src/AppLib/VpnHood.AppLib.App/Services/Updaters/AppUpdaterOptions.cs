using VpnHood.AppLib.Abstractions;

namespace VpnHood.AppLib.Services.Updaters;

public class AppUpdaterOptions
{
    public Uri? UpdateInfoUrl { get; init; }
    public IAppUpdaterProvider? UpdaterProvider { get; init; }
    public TimeSpan CheckInterval { get; init; } = TimeSpan.FromHours(24);
    public TimeSpan UpdateDelay { get; init; } = TimeSpan.FromDays(3);
    public TimeSpan PostponePeriod { get; init; } = TimeSpan.FromDays(7);
}