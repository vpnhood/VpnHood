using Avalonia;
using Avalonia.Media;

namespace VpnHood.App.StoreScreenshots;

// What a mockup stands on: the brand's own blue, darkening away from the top.
//
// Deliberately not a desktop wallpaper or a photograph - that artwork belongs to whoever made it,
// not in our listing - and deliberately the same behind every device, so a store page reads as one
// set rather than as a pile of screenshots.
internal static class Backdrop
{
    public static IBrush Brush { get; } = new RadialGradientBrush {
        Center = new RelativePoint(0.5, 0, RelativeUnit.Relative),
        GradientOrigin = new RelativePoint(0.5, 0, RelativeUnit.Relative),
        RadiusX = new RelativeScalar(1.2, RelativeUnit.Relative),
        RadiusY = new RelativeScalar(0.8, RelativeUnit.Relative),
        GradientStops = {
            new GradientStop(Color.Parse("#1c3fb0"), 0),
            new GradientStop(Color.Parse("#10206b"), 0.45),
            new GradientStop(Color.Parse("#070f38"), 1)
        }
    };
}
