using VpnHood.Client.Device.Droid.ActivityEvents;

namespace VpnHood.Client.Device.Droid;

public class AndroidUiContext(IActivityEvent activityEvent) : IUiContext
{
    public IActivityEvent ActivityEvent => activityEvent;
    public Activity Activity => activityEvent.Activity;
    public bool IsActive {
        get {
            try {
                return activityEvent.Activity.Window?.DecorView.RootView?.IsShown == true;
            }
            catch (Exception) {
                return false;
            }
        }
    }
}