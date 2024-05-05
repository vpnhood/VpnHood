using VpnHood.Client.App.Abstractions;
using VpnHood.Client.Device.Droid.Utils;

namespace VpnHood.Client.App.Droid.Common;

public class AndroidAppUiContext(IActivityEvent activityEvent) : IAppUiContext
{
    public IActivityEvent ActivityEvent => activityEvent;
    public Activity Activity => activityEvent.Activity;
}
