using Android.Window;

namespace VpnHood.AppLib.App.Android.Utils;

/// <summary>Provides a back callback for Android 13 and later, which calls an action.</summary>
/// <remarks>
/// It is a class of its own, made only on Android 13 and later: an older Android cannot load a class that implements the
/// interface, so an activity never implements it itself.
/// </remarks>
/// <param name="onBackInvoked">The action Back calls.</param>
public sealed class AndroidBackInvokedCallback(Action onBackInvoked)
    : Java.Lang.Object, IOnBackInvokedCallback
{
    /// <inheritdoc/>
    /// <remarks>It calls the action.</remarks>
    public void OnBackInvoked() => onBackInvoked();
}