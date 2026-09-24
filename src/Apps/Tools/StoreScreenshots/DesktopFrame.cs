using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using VpnHood.AppUi.Common;

namespace VpnHood.App.StoreScreenshots;

// The Microsoft Store's picture: the app's window standing on the brand backdrop.
//
// The window exists because the real one is far under the Store's floor (1366x768) - a capture of
// it alone would be rejected - and its chrome mirrors what a person actually sees: a title bar in
// the app's own background colour, sampled from the capture, a minimize, a maximize the window
// does not offer (the real one cannot be resized, so the glyph is dimmed) and a close.
internal static class DesktopFrame
{
    public static Control Build(DeviceSpec device, Bitmap capture, Color titleBarColor)
    {
        var canvas = device.Canvas ?? throw new InvalidOperationException($"{device.Label} is a desktop frame with no canvas.");
        var contentHeight = Math.Round(canvas.WindowWidth * ((double)device.CssHeight / device.CssWidth));

        var window = new Border {
            Width = canvas.WindowWidth,
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            BoxShadow = BoxShadows.Parse("0 30 70 #99000000, 0 8 22 #66000000"),
            Child = new StackPanel {
                Children = {
                    TitleBar(device, canvas, titleBarColor),
                    new Image {
                        Source = capture,
                        Width = canvas.WindowWidth,
                        Height = contentHeight,
                        Stretch = Stretch.UniformToFill
                    }
                }
            }
        };

        return new Panel {
            Width = canvas.Width,
            Height = canvas.Height,
            Background = Backdrop.Brush,
            Children = { window }
        };
    }

    private static Control TitleBar(DeviceSpec device, DesktopCanvas canvas, Color color)
    {
        var caption = new StackPanel {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { Button(Minimize()), Button(Maximize()), Button(Close()) }
        };

        var title = new TextBlock {
            Text = device.WindowTitle ?? "",
            FontFamily = FontFamily.Parse(AppFonts.TextFamily),
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            LetterSpacing = 0.2,
            Foreground = new SolidColorBrush(Color.Parse("#E0FFFFFF")),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0)
        };

        var bar = new Grid {
            Height = canvas.TitleBar,
            Background = new SolidColorBrush(color),
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Children = { title, caption }
        };
        Grid.SetColumn(caption, 1);
        return bar;
    }

    private static Control Button(Control glyph)
    {
        return new Panel { Width = 46, Children = { glyph } };
    }

    private static Control Minimize()
    {
        return new Rectangle {
            Width = 10, Height = 1, Fill = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static Control Maximize()
    {
        return new Rectangle {
            Width = 9, Height = 9, Stroke = Brushes.White, StrokeThickness = 1, Opacity = 0.35,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static Control Close()
    {
        var cross = new Panel {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 13,
            Height = 13
        };
        foreach (var angle in new double[] { 45, 135 })
            cross.Children.Add(new Rectangle {
                Width = 13, Height = 1, Fill = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                RenderTransform = new RotateTransform(angle)
            });
        return cross;
    }
}
