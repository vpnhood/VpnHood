using VpnHood.Core.Client.Device.Droid.ActivityEvents;
using VpnHood.Core.Client.Device.UiContexts;

namespace VpnHood.Core.Client.Device.Droid;

public class AndroidUiContext(IActivityEvent activityEvent) : IUiContext
{
    public IActivityEvent ActivityEvent => activityEvent;
    public Activity Activity => activityEvent.Activity;
    public bool IsDestroyed => activityEvent.Activity.IsDestroyed;
    public bool IsActive {
        get {
            try {
                return 
                    !activityEvent.Activity.IsDestroyed && 
                    activityEvent.Activity.Window?.DecorView.RootView?.IsShown == true;
            }
            catch (Exception) {
                return false;
            }
        }
    }
}