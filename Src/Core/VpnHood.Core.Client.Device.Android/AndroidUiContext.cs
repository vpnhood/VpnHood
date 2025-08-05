using VpnHood.Core.Client.Device.Droid.ActivityEvents;
using VpnHood.Core.Client.Device.Droid.Utils;
using VpnHood.Core.Client.Device.UiContexts;

namespace VpnHood.Core.Client.Device.Droid;

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
                await AndroidUtil.RunOnUiThread(activityEvent.Activity, () =>
                    !activityEvent.Activity.IsDestroyed &&
                    activityEvent.Activity.Window?.DecorView.RootView?.IsShown == true
                );
        }
        catch {
            return false;
        }
    }
}