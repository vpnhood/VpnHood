using VpnHood.Core.Client.Devices.UiContexts;

namespace VpnHood.AppLib.Ios.Common;

// Minimal iOS UI context. The host app is a single foreground UIViewController, so the context is
// always considered active and never destroyed while the app is running. It is published to
// AppUiContext.Context so the core/web-server can request UI-bound operations.
public class IosUiContext : IUiContext
{
    public Task<bool> IsDestroyed() => Task.FromResult(false);
    public Task<bool> IsActive() => Task.FromResult(true);
}
