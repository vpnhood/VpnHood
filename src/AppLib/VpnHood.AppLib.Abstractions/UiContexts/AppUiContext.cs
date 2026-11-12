using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;

namespace VpnHood.AppLib.Abstractions.UiContexts;

/// <summary>Provides the app's current UI context, which the platform sets while its UI is up.</summary>
public static class AppUiContext
{
    /// <summary>Occurs when <see cref="Context"/> changes.</summary>
    public static event EventHandler? OnChanged;

    /// <summary>
    /// Occurs when the app comes back to the foreground, as each platform raises it from its own resume (iOS's
    /// WillEnterForeground, Android's OnResume, Windows' Activated).
    /// </summary>
    /// <remarks>
    /// The local web server listens to it, to mend a loopback listener the OS may have torn down while the app was in
    /// the background, as iOS does.
    /// </remarks>
    public static event EventHandler? OnResumed;

    /// <summary>
    /// Gets or sets the app's UI context, as the platform supplies it, or <see langword="null"/> while it supplies none.
    /// </summary>
    /// <remarks>
    /// A context may stay while its window is hidden, as the desktop's does: whether the UI is in front is the context's
    /// own <see cref="IUiContext.IsActive"/>.
    /// </remarks>
    public static IUiContext? Context {
        get;
        set {
            if (field == value) return;
            field = value;
            OnChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>Raises <see cref="OnResumed"/>, as the platform does when the app comes back to the foreground.</summary>
    /// <remarks>It may be called from any thread; a listener moves its own work off the caller's.</remarks>
    public static void NotifyResumed() => OnResumed?.Invoke(null, EventArgs.Empty);

    /// <summary>Gets the app's UI context, as <see cref="Context"/> gives it.</summary>
    /// <exception cref="UiContextNotAvailableException">No UI context is set.</exception>
    public static IUiContext RequiredContext => Context ?? throw new UiContextNotAvailableException();

    /// <summary>
    /// Gets a value indicating whether a partial intent is running: a system screen over the app's, as a permission's,
    /// which pauses the app's own while it stays in front.
    /// </summary>
    public static bool IsPartialIntentRunning => PartialIntentScope.IsRunning;

    /// <summary>Marks a partial intent as running until the scope is disposed.</summary>
    /// <returns>The scope.</returns>
    public static IDisposable CreatePartialIntentScope() => new PartialIntentScope();
}