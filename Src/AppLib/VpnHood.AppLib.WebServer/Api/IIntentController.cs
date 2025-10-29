namespace VpnHood.AppLib.WebServer.Api;

public interface IIntentController
{
    Task<bool> RequestNotification();
    Task<bool> RequestQuickLaunch();
    Task RequestUserReview();
    Task OpenAlwaysOnSettings();
    Task OpenSettings();
    Task OpenAppSettings();
    Task OpenAppNotificationSettings();
}