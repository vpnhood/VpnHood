using Avalonia;

namespace VpnHood.App.StoreScreenshots;

// Avalonia with nothing in it, for the framing pass: no window, no UI, no app state - the mockups
// are built in code and drawn into a bitmap. It exists because a render target needs a platform,
// and the store's fonts need an application to be registered against.
internal sealed class FrameApp : Application;
