using VpnHood.Client.Device.Droid.ActivityEvents;

namespace VpnHood.Client.Device.Droid;

public class AndroidUiContext(IActivityEvent activityEvent) : IUiContext
{
    public IActivityEvent ActivityEvent => activityEvent;
    public Activity Activity => activityEvent.Activity;
}