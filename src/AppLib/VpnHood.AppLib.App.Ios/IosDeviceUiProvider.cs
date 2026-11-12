using VpnHood.AppLib.Abstractions.Device;
using VpnHood.Core.Client.Devices.Ios.Utils;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;

namespace VpnHood.AppLib.App.Ios;

// iOS implementation of the device UI provider. Most platform-integration points (quick launch,
// notifications, private DNS, kill switch) are not applicable to a regular iOS VPN app, so they
// fall back to the Null implementation. We only wire up opening the iOS Settings app.
internal class IosDeviceUiProvider : NullDeviceUiProvider
{
    // The UI renders edge-to-edge, so it needs the status-bar / home-indicator inset sizes to pad
    // itself. Mirrors AndroidDeviceUiProvider.GetBarsInfo. Heights are reported in PHYSICAL PIXELS
    // (points * screen scale), which a UI converts back by the screen scale it draws at.
    public override SystemBarsInfo GetBarsInfo(IUiContext uiContext)
    {
        // A TV has no bars: tvOS's safe area is overscan, which a TV page keeps clear by itself
        // (AppTheme's .tv .page), and a paired phone's browser takes these heights as its own.
        if (OperatingSystem.IsTvOS())
            return SystemBarsInfo.Default;

        // BuildAppState may call this off the main thread; SafeAreaInsets must be read on the UI thread.
        return IosUtils.RunOnUiThread(() => {
            var window = GetKeyWindow();
            if (window == null)
                return SystemBarsInfo.Default;

            // iPadOS 26 puts a window's controls in its top-leading corner, outside the plain safe
            // area, over the UI's menu button: the safe area adapted to that corner vertically has
            // its top below them.
            var insets = OperatingSystem.IsIOSVersionAtLeast(26) || OperatingSystem.IsTvOSVersionAtLeast(26)
                ? window.GetEdgeInsets(UIViewLayoutRegion.CreateSafeAreaLayoutRegion(UIViewLayoutRegionAdaptivityAxis.Vertical))
                : window.SafeAreaInsets;
            var scale = window.Screen.Scale;
            return new SystemBarsInfo {
                TopHeight = (int)Math.Ceiling(insets.Top * scale),
                BottomHeight = (int)Math.Ceiling(insets.Bottom * scale)
            };
        }).GetAwaiter().GetResult();
    }

    public override Task KeepScreenOn(IUiContext uiContext, bool value)
    {
        return IosUtils.RunOnUiThread(() => {
            UIApplication.SharedApplication.IdleTimerDisabled = value;
        });
    }

    public override bool IsAppSettingsSupported => true;

    public override void OpenAppSettings(IUiContext context)
    {
        OpenUrl(UIApplication.OpenSettingsUrlString);
    }

    public override bool IsSettingsSupported => true;

    public override void OpenSettings(IUiContext uiContext)
    {
        OpenUrl(UIApplication.OpenSettingsUrlString);
    }

#if !__TVOS__
    public override bool IsShareSupported => true;

    public override void ShareFile(IUiContext uiContext, string fileName, byte[] content)
    {
        var filePath = Path.Combine(Path.GetTempPath(), fileName);
        File.WriteAllBytes(filePath, content);

        IosUtils.RunOnUiThread(() => {
            var presenter = GetKeyWindow()?.RootViewController ??
                            throw new InvalidOperationException("There is no window to share from.");
            while (presenter.PresentedViewController is { } presented)
                presenter = presented;

            var sheet = new UIActivityViewController([NSUrl.FromFilename(filePath)], applicationActivities: null);

            // an iPad shows the sheet as a popover, which needs something to point at
            if (sheet.PopoverPresentationController is { } popover && presenter.View is { } view) {
                popover.SourceView = view;
                popover.SourceRect = new CGRect(view.Bounds.GetMidX(), view.Bounds.GetMidY(), 0, 0);
                popover.PermittedArrowDirections = 0;
            }

            presenter.PresentViewController(sheet, animated: true, completionHandler: null);
        }).GetAwaiter().GetResult();
    }
#endif

    private static void OpenUrl(string urlString)
    {
        var url = new NSUrl(urlString);
        UIApplication.SharedApplication.OpenUrl(url, new NSDictionary(), null);
    }

    // The active foreground key window across the connected scenes (the UI's window).
    private static UIWindow? GetKeyWindow()
    {
        return UIApplication.SharedApplication.ConnectedScenes
            .OfType<UIWindowScene>()
            .Where(scene => scene.ActivationState == UISceneActivationState.ForegroundActive)
            .SelectMany(scene => scene.Windows)
            .FirstOrDefault(w => w.IsKeyWindow)
            ?? UIApplication.SharedApplication.Windows.FirstOrDefault(w => w.IsKeyWindow);
    }
}
