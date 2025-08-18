namespace VpnHood.AppLib.WebServer.Api;

public interface IIntentController
{
    Task RequestNotification();
    Task RequestQuickLaunch();
    Task RequestUserReview();
    Task OpenSystemAlwaysOnSettings();
    Task OpenSystemSettings();
    Task OpenAppSystemSettings();
    Task OpenAppSystemNotificationSettings();
}