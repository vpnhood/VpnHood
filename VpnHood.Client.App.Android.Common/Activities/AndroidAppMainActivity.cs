using VpnHood.Client.Device.Droid.ActivityEvents;

namespace VpnHood.Client.App.Droid.Common.Activities;

public abstract class AndroidAppMainActivity : ActivityEvent
{
    protected AndroidAppMainActivityHandler MainActivityHandler = default!;
    protected abstract AndroidAppMainActivityHandler CreateMainActivityHandler();

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        MainActivityHandler = CreateMainActivityHandler(); // must before base.OnCreate to make sure event is fired
        base.OnCreate(savedInstanceState);
    }
}
