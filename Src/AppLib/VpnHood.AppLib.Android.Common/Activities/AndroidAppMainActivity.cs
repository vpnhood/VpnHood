using VpnHood.Core.Client.Device.Droid.ActivityEvents;

namespace VpnHood.AppLib.Droid.Common.Activities;

public abstract class AndroidAppMainActivity : ActivityEvent
{
    protected AndroidAppMainActivityHandler? MainActivityHandler { get; private set; }
    protected abstract AndroidAppMainActivityHandler CreateMainActivityHandler();

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // must before base.OnCreate to make sure event is fired
        MainActivityHandler = CreateMainActivityHandler(); 
        base.OnCreate(savedInstanceState);
    }
}