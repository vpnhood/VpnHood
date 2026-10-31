using Android.Content;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Abstractions.Updaters;
using VpnHood.Core.Client.Devices.Android;
using VpnHood.Core.Client.Devices.Android.Utils;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Net.Toolkit.Logging;
using AndroidUri = Android.Net.Uri;

namespace VpnHood.AppLib.Stores.GalaxyStore;

// Galaxy Store has no update API for an app, and its rules allow no update from anywhere else: the
// feed tells of the newer release, and the notice's Update opens the app's Galaxy Store page, where the
// person updates or not. The feed is the Galaxy Store release's own, as the store's review lets a
// release out later than Play's: a notice for a version still in review would open a page without it.
public class GalaxyStoreAppUpdaterProvider : IAppUpdaterProvider
{
    // the store page, which only Galaxy Store opens: an install from it always has it
    private const string ProductDetailUrl = "samsungapps://ProductDetail/";

    // the notice offers the update: Galaxy Store has no screen of its own for it to open
    public bool HasOwnScreen => false;

    // Galaxy Store installs it, and stops the app itself
    public bool IsInstalling => false;

    public Task<AppUpdateState> Check(AppRelease? feedRelease, IUiContext? uiContext,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(GetState(feedRelease));
    }

    public AppUpdateState GetState(AppRelease? feedRelease)
    {
        return feedRelease is null
            ? AppUpdateState.None
            : new AppUpdateState { Stage = AppUpdateStage.Available };
    }

    public Task StartUpdate(AppRelease? feedRelease, IUiContext? uiContext,
        CancellationToken cancellationToken)
    {
        var activity = ((AndroidUiContext)(uiContext ?? throw new UiContextNotAvailableException())).Activity;
        var packageName = activity.PackageName ?? throw new InvalidOperationException("The app has no package name.");

        VhLogger.Instance.LogDebug("Opening the Galaxy Store page for update...");
        return AndroidUtils.RunOnUiThread(activity, () => {
            using var intent = new Intent(Intent.ActionView, AndroidUri.Parse(ProductDetailUrl + packageName));
            activity.StartActivity(intent);
        });
    }

    // Galaxy Store installs on its own page: nothing waits for the app's Install
    public Task CompleteUpdate(AppRelease? feedRelease, IUiContext? uiContext,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Galaxy Store installs its update on its own page.");
    }

    // the update runs in the Galaxy Store app, so the app has none of its own to bring back
    public Task<bool> ResumeUpdate(IUiContext uiContext, CancellationToken cancellationToken)
    {
        return Task.FromResult(false);
    }

    public bool IsAutomaticUpdateSupported => false;

    public Task<bool> IsAutomaticUpdateOn(CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Galaxy Store keeps its own automatic update setting.");
    }

    public Task SetAutomaticUpdate(bool isOn, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Galaxy Store keeps its own automatic update setting.");
    }
}
