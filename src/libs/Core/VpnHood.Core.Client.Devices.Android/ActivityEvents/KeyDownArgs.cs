using Android.Views;

namespace VpnHood.Core.Client.Devices.Android.ActivityEvents;

/// <summary>
/// Provides the data of <see cref="IActivityEvent.KeyDownEvent"/>, where a handler that handles the key says so.
/// </summary>
public class KeyDownArgs
{
    /// <summary>Gets the key's code.</summary>
    public required Keycode KeyCode { get; init; }

    /// <summary>Gets the key's event, or <see langword="null"/>.</summary>
    public required KeyEvent? KeyEvent { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether a handler has handled the key, so the activity leaves it.
    /// </summary>
    public bool IsHandled { get; set; }
}