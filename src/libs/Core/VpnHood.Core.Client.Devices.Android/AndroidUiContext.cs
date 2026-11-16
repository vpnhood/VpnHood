using VpnHood.Core.Client.Devices.Android.ActivityEvents;
using VpnHood.Core.Client.Devices.Android.Utils;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Core.Client.Devices.Android;

/// <summary>
/// Represents the app's UI on Android: an activity, in which the device shows its prompts and the app its ads.
/// </summary>
/// <param name="activityEvent">The activity, with its events.</param>
public class AndroidUiContext(IActivityEvent activityEvent) : IUiContext
{
    /// <summary>Gets the activity, with its events.</summary>
    public IActivityEvent ActivityEvent => activityEvent;

    /// <summary>Gets the activity.</summary>
    public Activity Activity => activityEvent.Activity;

    /// <inheritdoc/>
    /// <remarks>It is the activity's own answer; <see langword="false"/> where that cannot be read.</remarks>
    public Task<bool> IsDestroyed()
    {
        try {
            return Task.FromResult(activityEvent.Activity.IsDestroyed);
        }
        catch {
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It asks on the UI thread whether the activity is not destroyed and its window's view is shown;
    /// <see langword="false"/> where that cannot be read.
    /// </remarks>
    public async Task<bool> IsActive()
    {
        try {
            return
                await AndroidUtils.RunOnUiThread(activityEvent.Activity, () =>
                    !activityEvent.Activity.IsDestroyed &&
                    activityEvent.Activity.Window?.DecorView.RootView?.IsShown == true
                ).Vhc();
        }
        catch {
            return false;
        }
    }
}