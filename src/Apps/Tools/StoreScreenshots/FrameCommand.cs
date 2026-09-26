using Avalonia;
using Avalonia.Headless;
using VpnHood.AppUi.Presentation.Classic.Avalonia;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Resources;

namespace VpnHood.App.StoreScreenshots;

// The framing pass: a capture in, the store's file out. No page is drawn here and no app state is
// read - only the store's fonts, for the status bar and the window title, which are the app's own
// face rather than whatever face the machine happens to have.
internal static class FrameCommand
{
    public static int Run(IReadOnlyList<string> args)
    {
        FrameOptions options;
        try {
            options = FrameOptions.Parse(args);
        }
        catch (ArgumentException ex) {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }

        try {
            ClassicAvaloniaApp.PrepareContentAsync(Store.Open(options.AssetsPath), CancellationToken.None)
                .GetAwaiter().GetResult();
            AppBuilder.Configure<FrameApp>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .UseSkia()
                .SetupWithoutStarting();
            AppAssets.RegisterFonts();

            var device = DeviceSpec.Load(options.DevicePath);
            var (width, height) = FrameRenderer.Render(device, options.InputPath, options.OutputPath);
            Console.WriteLine($"framed    {Path.GetFileName(options.OutputPath)}  {width}x{height}  {device.Label}");
            return 0;
        }
        catch (Exception ex) {
            Console.Error.WriteLine($"FAILED    {Path.GetFileName(options.OutputPath)}: {ex.Message}");
            return 1;
        }
    }
}
