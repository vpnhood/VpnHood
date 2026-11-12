using Android.OS;
using VpnHood.AppLib.App.Android.Activities;

// ReSharper disable once CheckNamespace
namespace VpnHood.AppUi.Hosting.Maui;

/// <summary>
/// Represents a MAUI UI's main activity on Android: a head derives from it and makes the app's main activity handler.
/// </summary>
public abstract class VpnHoodMauiMainActivity : MauiActivityEvent
{
    /// <summary>
    /// Creates the handler that does the activity's work for the app; it must be made for this activity, as it follows the
    /// activity's events, through which it lives on: the returned handler is not stored.
    /// </summary>
    /// <returns>The handler.</returns>
    protected abstract AndroidAppMainActivityHandler CreateMainActivityHandler();

    /// <inheritdoc/>
    /// <remarks>It makes the handler first, so the handler sees the create event.</remarks>
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        CreateMainActivityHandler();
        base.OnCreate(savedInstanceState);
    }
}