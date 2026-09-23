namespace VpnHood.AppLib.Api;

public interface IIntentsApi
{
    Task<bool> RequestNotification(CancellationToken cancellationToken);
    Task<bool> RequestQuickLaunch(CancellationToken cancellationToken);
    Task RequestUserReview(CancellationToken cancellationToken);
    Task OpenKillSwitchSettings(CancellationToken cancellationToken);
    Task OpenAlwaysOnSettings(CancellationToken cancellationToken);
    Task OpenSettings(CancellationToken cancellationToken);
    Task OpenAppSettings(CancellationToken cancellationToken);
    Task OpenAppNotificationSettings(CancellationToken cancellationToken);
}