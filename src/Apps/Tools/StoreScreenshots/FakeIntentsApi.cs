using VpnHood.AppLib.Api;

namespace VpnHood.App.StoreScreenshots;

// There is no device whose settings could open: every intent is a call a screenshot never makes.
internal sealed class FakeIntentsApi : IIntentsApi
{
    public Task<bool> RequestNotification(CancellationToken cancellationToken) => throw UnmockedCalls.Record("Intents.RequestNotification");
    public Task<bool> RequestQuickLaunch(CancellationToken cancellationToken) => throw UnmockedCalls.Record("Intents.RequestQuickLaunch");
    public Task RequestUserReview(CancellationToken cancellationToken) => throw UnmockedCalls.Record("Intents.RequestUserReview");
    public Task OpenKillSwitchSettings(CancellationToken cancellationToken) => throw UnmockedCalls.Record("Intents.OpenKillSwitchSettings");
    public Task OpenAlwaysOnSettings(CancellationToken cancellationToken) => throw UnmockedCalls.Record("Intents.OpenAlwaysOnSettings");
    public Task OpenSettings(CancellationToken cancellationToken) => throw UnmockedCalls.Record("Intents.OpenSettings");
    public Task OpenAppSettings(CancellationToken cancellationToken) => throw UnmockedCalls.Record("Intents.OpenAppSettings");
    public Task OpenAppNotificationSettings(CancellationToken cancellationToken) => throw UnmockedCalls.Record("Intents.OpenAppNotificationSettings");
}
