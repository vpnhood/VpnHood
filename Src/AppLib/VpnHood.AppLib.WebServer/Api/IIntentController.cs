namespace VpnHood.AppLib.WebServer.Api;

public interface IIntentController
{
    Task<bool> RequestNotification();
    Task<bool> RequestQuickLaunch();
    Task RequestUserReview();
    Task OpenSystemAlwaysOnSettings();
    Task OpenSystemSettings();
    Task OpenAppSystemSettings();
    Task OpenAppSystemNotificationSettings();
}