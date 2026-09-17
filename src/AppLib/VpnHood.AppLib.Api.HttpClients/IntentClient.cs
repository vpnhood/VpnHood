
namespace VpnHood.AppLib.Api.HttpClients;

// IIntentsApi over HTTP: the routes of IntentsController, one for one. Every one of them
// opens something on the device that runs the app, not on the one that asked.
internal sealed class IntentClient(HttpClient httpClient) : AppApiClientBase(httpClient), IIntentsApi
{
    private const string BaseUrl = "api/intents/";

    public Task<bool> RequestNotification(CancellationToken cancellationToken)
    {
        return HttpPostAsync<bool>(BaseUrl + "request-notification", null, null, cancellationToken);
    }

    public Task<bool> RequestQuickLaunch(CancellationToken cancellationToken)
    {
        return HttpPostAsync<bool>(BaseUrl + "request-quick-launch", null, null, cancellationToken);
    }

    public Task RequestUserReview(CancellationToken cancellationToken)
    {
        return HttpPostAsync(BaseUrl + "request-user-review", null, null, cancellationToken);
    }

    public Task OpenKillSwitchSettings(CancellationToken cancellationToken)
    {
        return HttpPostAsync(BaseUrl + "open-kill-switch-settings", null, null, cancellationToken);
    }

    public Task OpenAlwaysOnSettings(CancellationToken cancellationToken)
    {
        return HttpPostAsync(BaseUrl + "open-always-on-settings", null, null, cancellationToken);
    }

    public Task OpenSettings(CancellationToken cancellationToken)
    {
        return HttpPostAsync(BaseUrl + "open-settings", null, null, cancellationToken);
    }

    public Task OpenAppSettings(CancellationToken cancellationToken)
    {
        return HttpPostAsync(BaseUrl + "open-app-settings", null, null, cancellationToken);
    }

    public Task OpenAppNotificationSettings(CancellationToken cancellationToken)
    {
        return HttpPostAsync(BaseUrl + "open-app-notification-settings", null, null, cancellationToken);
    }
}
