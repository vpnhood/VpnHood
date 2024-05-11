using VpnHood.Client.Device.Droid.Utils;

namespace VpnHood.Client.Device.Droid;

public class AndroidUiContext(IActivityEvent activityEvent) : IUiContext
{
    public IActivityEvent ActivityEvent => activityEvent;
    public Activity Activity => activityEvent.Activity;
}
