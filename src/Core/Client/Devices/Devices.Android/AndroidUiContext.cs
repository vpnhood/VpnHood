using VpnHood.Core.Client.Devices.Droid.ActivityEvents;
using VpnHood.Core.Client.Devices.Droid.Utils;
using VpnHood.Core.Client.Devices.UiContexts;

namespace VpnHood.Core.Client.Devices.Droid;

public class AndroidUiContext(IActivityEvent activityEvent) : IUiContext
{
    public IActivityEvent ActivityEvent => activityEvent;
    public Activity Activity => activityEvent.Activity;

    public Task<bool> IsDestroyed()
    {
        try {
            return Task.FromResult(activityEvent.Activity.IsDestroyed);
        }
        catch {
            return Task.FromResult(false);
        }
    }

    public async Task<bool> IsActive()
    {
        try {
            return
                await AndroidUtils.RunOnUiThread(activityEvent.Activity, () =>
                    !activityEvent.Activity.IsDestroyed &&
                    activityEvent.Activity.Window?.DecorView.RootView?.IsShown == true
                );
        }
        catch {
            return false;
        }
    }
}