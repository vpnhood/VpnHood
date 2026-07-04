using VpnHood.Core.Common.Exceptions;

namespace VpnHood.Core.Client.Devices.UiContexts;

public static class AppUiContext
{
    public static event EventHandler? OnChanged;

    // Raised when the host app returns to the foreground / becomes active. Each platform raises it
    // from its native resume hook (iOS WillEnterForeground, Android OnResume, Windows Activated).
    // The SPA web server subscribes to this to self-heal a loopback listener that iOS (or, more
    // rarely, another OS) may have torn down while the app was backgrounded.
    public static event EventHandler? OnResumed;

    public static IUiContext? Context {
        get;
        set {
            if (field == value) return;
            field = value;
            OnChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    // Signal that the host app has resumed to the foreground. Safe to call from any thread; the
    // main use (rebinding the web-server socket) is marshalled off the caller by the subscriber.
    public static void NotifyResumed() => OnResumed?.Invoke(null, EventArgs.Empty);

    public static IUiContext RequiredContext => Context ?? throw new UiContextNotAvailableException();
    public static bool IsPartialIntentRunning => PartialIntentScope.IsRunning;

    // Partial activities may make the main activity animation pause
    public static IDisposable CreatePartialIntentScope() => new PartialIntentScope();
}