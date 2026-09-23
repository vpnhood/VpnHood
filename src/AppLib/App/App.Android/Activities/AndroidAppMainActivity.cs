using Android.Content;
using VpnHood.Core.Client.Devices.Android.ActivityEvents;

namespace VpnHood.AppLib.App.Android.Activities;

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

    // The creation of a launch that another activity takes instead: a head that ships a second UI
    // sends the launch to that UI's activity. Called from OnCreate in place of base.OnCreate,
    // which is why it takes the bundle - Android throws SuperNotCalledException for an OnCreate
    // that returns without its super call, and the one to reach is ActivityEvent's, above the
    // handler this class builds: no main activity handler for an activity about to finish, so
    // nothing is mounted and the access key is imported once, by the activity that stays.
    // The intent comes along, so that key arrives there all the same.
    protected void OnCreateRedirectingTo<TActivity>(Bundle? savedInstanceState)
        where TActivity : Activity
    {
        base.OnCreate(savedInstanceState);
        var intent = Intent != null ? new Intent(Intent) : new Intent();
        intent.SetClass(this, Java.Lang.Class.FromType(typeof(TActivity)));
        StartActivity(intent);
        Finish();
    }
}
