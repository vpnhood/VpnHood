using Android.Content;
using VpnHood.Core.Client.Devices.Android.ActivityEvents;

namespace VpnHood.AppLib.App.Android.Activities;

/// <summary>
/// Represents a head's main activity that leaves its work to the app's main activity handler, which it makes as it is
/// created (<see cref="CreateMainActivityHandler"/>).
/// </summary>
public abstract class AndroidAppMainActivity : ActivityEvent
{
    /// <summary>Gets the activity's handler, once it is made, or <see langword="null"/>.</summary>
    private AndroidAppMainActivityHandler? MainActivityHandler { get; set; }

    /// <summary>Creates the handler that does the activity's work for the app.</summary>
    /// <returns>The handler.</returns>
    protected abstract AndroidAppMainActivityHandler CreateMainActivityHandler();

    /// <inheritdoc/>
    /// <remarks>It makes the handler before the base raises the create event, so the handler sees it.</remarks>
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // must before base.OnCreate to make sure event is fired
        MainActivityHandler = CreateMainActivityHandler();
        base.OnCreate(savedInstanceState);
    }

    /// <summary>
    /// Creates this activity only to hand its launch, with its intent, to another activity, and finishes: for a head that
    /// ships a second UI. A head calls it from <see cref="OnCreate"/> in place of the base's.
    /// </summary>
    /// <typeparam name="TActivity">The activity that takes the launch.</typeparam>
    /// <param name="savedInstanceState">The state Android saved, as <see cref="OnCreate"/> was given it.</param>
    /// <remarks>
    /// No handler is made for an activity about to finish; the intent goes along, so an access key in it reaches the
    /// activity that takes the launch, where that one handles it.
    /// </remarks>
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
