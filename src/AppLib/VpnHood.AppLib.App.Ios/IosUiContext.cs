using VpnHood.Core.Client.Devices.Abstractions.UiContexts;

namespace VpnHood.AppLib.App.Ios;

// Minimal iOS UI context. The host app is a single foreground UIViewController, so the context is
// always considered active and never destroyed while the app is running. It is published to
// AppUiContext.Context so the core/web-server can request UI-bound operations.
/// <summary>
/// Represents the iOS app's UI context: it reports the app as active and not destroyed whenever it is asked, as the app
/// has one view controller for its whole run.
/// </summary>
public class IosUiContext : IUiContext
{
    /// <inheritdoc/>
    /// <remarks>It always gives <see langword="false"/>.</remarks>
    public Task<bool> IsDestroyed() => Task.FromResult(false);
    /// <inheritdoc/>
    /// <remarks>It always gives <see langword="true"/>.</remarks>
    public Task<bool> IsActive() => Task.FromResult(true);
}
