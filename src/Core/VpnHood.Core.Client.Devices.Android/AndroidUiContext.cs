using VpnHood.Core.Client.Devices.Android.ActivityEvents;
using VpnHood.Core.Client.Devices.Android.Utils;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Core.Client.Devices.Android;

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
                ).Vhc();
        }
        catch {
            return false;
        }
    }
}