namespace VpnHood.AppLib.WebServer.Api;

public interface IIntentController
{
    Task RequestQuickLaunch();
    Task RequestNotification();
    Task RequestUserReview();
    Task OpenSystemAlwaysOnSettings();
    Task OpenSystemSettings();
    Task OpenAppSystemSettings();
    Task OpenAppSystemNotificationSettings();
}