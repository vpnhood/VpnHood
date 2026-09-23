using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Resources;

// An image of the asset store on an Image or an ImageBrush, in XAML:
//
//     <Image ui:AppImage.Source="images/rocket.webp" />
//
// The store is read through a provider that may be a web server, so the bitmap cannot be handed
// to a binding when it is asked for: the control shows, the bytes arrive, the source is set. A
// path that changed under a row a list recycled is not overtaken by the earlier picture.
public static class AppImage
{
    public static readonly AttachedProperty<string?> SourceProperty =
        AvaloniaProperty.RegisterAttached<AvaloniaObject, string?>("Source", typeof(AppImage));

    static AppImage()
    {
        SourceProperty.Changed.AddClassHandler<AvaloniaObject>(OnSourceChanged);
    }

    public static string? GetSource(AvaloniaObject target)
    {
        return target.GetValue(SourceProperty);
    }

    public static void SetSource(AvaloniaObject target, string? value)
    {
        target.SetValue(SourceProperty, value);
    }

    private static async void OnSourceChanged(AvaloniaObject target, AvaloniaPropertyChangedEventArgs e)
    {
        var path = e.NewValue as string;
        try {
            Apply(target, null);
            if (path == null)
                return;

            var bitmap = await AppAssets.LoadBitmapAsync(path);
            if (GetSource(target) == path)
                Apply(target, bitmap);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not load the image. Path: {Path}", path);
        }
    }

    private static void Apply(AvaloniaObject target, Bitmap? bitmap)
    {
        switch (target) {
            case Image image:
                image.Source = bitmap;
                break;
            case ImageBrush brush:
                brush.Source = bitmap;
                break;
            default:
                throw new NotSupportedException($"{nameof(AppImage)} draws on an Image or an ImageBrush, not on a {target.GetType().Name}.");
        }
    }
}
