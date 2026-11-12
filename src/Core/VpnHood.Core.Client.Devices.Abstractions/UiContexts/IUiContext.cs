namespace VpnHood.Core.Client.Devices.Abstractions.UiContexts;

/// <summary>
/// Defines the app's UI as the device sees it, as an activity on Android, in which the device shows its prompts and
/// the app its ads.
/// </summary>
public interface IUiContext
{
    /// <summary>
    /// Determines whether the UI is gone, as far as the platform tells: a desktop reports a hidden window as gone, and
    /// iOS never reports one.
    /// </summary>
    /// <returns>A task whose result is <see langword="true"/> if it is; otherwise, <see langword="false"/>.</returns>
    Task<bool> IsDestroyed();

    /// <summary>
    /// Determines whether the UI is shown to the user, as far as the platform tells: Android checks its view, and iOS
    /// always answers <see langword="true"/>.
    /// </summary>
    /// <returns>A task whose result is <see langword="true"/> if it is; otherwise, <see langword="false"/>.</returns>
    Task<bool> IsActive();
};