using VpnHood.AppLib.WebServer.Api;

namespace VpnHood.AppLib.WebServer.Client;

// IIntentController over HTTP: the routes of IntentsController, one for one. Every one of them
// opens something on the device that runs the app, not on the one that asked.
internal sealed class IntentClient(HttpClient httpClient) : AppApiClientBase(httpClient), IIntentController
{
    private const string BaseUrl = "api/intents/";

    public Task<bool> RequestNotification(CancellationToken cancellationToken)
    {
        return PostAsync<bool>(BaseUrl + "request-notification", null, cancellationToken);
    }

    public Task<bool> RequestQuickLaunch(CancellationToken cancellationToken)
    {
        return PostAsync<bool>(BaseUrl + "request-quick-launch", null, cancellationToken);
    }

    public Task RequestUserReview(CancellationToken cancellationToken)
    {
        return PostAsync(BaseUrl + "request-user-review", null, cancellationToken);
    }

    public Task OpenKillSwitchSettings(CancellationToken cancellationToken)
    {
        return PostAsync(BaseUrl + "open-kill-switch-settings", null, cancellationToken);
    }

    public Task OpenAlwaysOnSettings(CancellationToken cancellationToken)
    {
        return PostAsync(BaseUrl + "open-always-on-settings", null, cancellationToken);
    }

    public Task OpenSettings(CancellationToken cancellationToken)
    {
        return PostAsync(BaseUrl + "open-settings", null, cancellationToken);
    }

    public Task OpenAppSettings(CancellationToken cancellationToken)
    {
        return PostAsync(BaseUrl + "open-app-settings", null, cancellationToken);
    }

    public Task OpenAppNotificationSettings(CancellationToken cancellationToken)
    {
        return PostAsync(BaseUrl + "open-app-notification-settings", null, cancellationToken);
    }
}
