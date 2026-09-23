using System.Text.Json;
using System.Text.Json.Serialization;

namespace VpnHood.App.StoreScreenshots;

// One device of a store's set: the viewport its captures are taken at, and the dress they are put
// in afterwards. The field names are the ones the store configuration uses, so a device reads the
// same in the configuration and here.
//
// CssWidth x CssHeight is the device's screen in logical pixels and Scale its pixel ratio, so the
// store file is CssWidth*Scale x CssHeight*Scale (a phone mockup and a bare capture) or the
// Canvas (a desktop scene). A capture covers the safe strip alone - the bands of a phone mockup
// paint the status bar and the home indicator over the rest - which is also how the capture is
// sized: CssWidth x (CssHeight - SafeTop - SafeBottom).
internal sealed class DeviceSpec
{
    public required string Label { get; init; }

    // What its files are called apart from the other devices of the same store folder ("ipad_").
    public string Prefix { get; init; } = "";

    public required FrameKind Frame { get; init; }
    public required int CssWidth { get; init; }
    public required int CssHeight { get; init; }
    public required double Scale { get; init; }
    public int SafeTop { get; init; }
    public int SafeBottom { get; init; }

    // --- a phone mockup's geometry, in the device's own logical pixels ---
    public double ScreenW { get; init; }
    public double Bezel { get; init; }
    public double OuterRadius { get; init; }
    public double ScreenRadius { get; init; }
    public DeviceIsland? Island { get; init; }
    public DevicePunchHole? PunchHole { get; init; }
    public double StatusFont { get; init; }
    public double StatusPad { get; init; }
    public double IndicatorWidth { get; init; }

    // "android" leads the status cluster with the wifi fan, as that OS does; an iPhone shows
    // signal and battery alone.
    public string? StatusStyle { get; init; }

    // --- a desktop scene ---
    public DesktopCanvas? Canvas { get; init; }
    public string? WindowTitle { get; init; }

    public bool IsAndroidStatus => StatusStyle == "android";

    // The store's own file size: a desktop scene is the canvas, everything else the screen.
    public (int Width, int Height) FinalSize => Frame == FrameKind.Desktop
        ? (Canvas?.Width ?? throw new InvalidOperationException($"{Label} is a desktop frame with no canvas."),
            Canvas.Height)
        : ((int)Math.Round(CssWidth * Scale), (int)Math.Round(CssHeight * Scale));

    public static DeviceSpec Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<DeviceSpec>(json, Options)
               ?? throw new InvalidOperationException($"{path} holds no device.");
    }

    private static JsonSerializerOptions Options { get; } = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}
