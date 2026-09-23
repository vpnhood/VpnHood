namespace VpnHood.App.StoreScreenshots;

// One capture to dress, as the command line describes it.
//
//   VpnHoodStoreScreenshots frame --device <device.json> --assets <store folder or ui.zip>
//       --in raw/android-phone/1_en-US.png --out final/android-phone/1_en-US.png
internal sealed class FrameOptions
{
    public required string DevicePath { get; init; }
    public required string AssetsPath { get; init; }
    public required string InputPath { get; init; }
    public required string OutputPath { get; init; }

    public static FrameOptions Parse(IReadOnlyList<string> args)
    {
        var values = CommandLine.Parse(args);
        return new FrameOptions {
            DevicePath = Path.GetFullPath(CommandLine.Required(values, "device")),
            AssetsPath = Path.GetFullPath(CommandLine.Required(values, "assets")),
            InputPath = Path.GetFullPath(CommandLine.Required(values, "in")),
            OutputPath = Path.GetFullPath(CommandLine.Required(values, "out"))
        };
    }
}
