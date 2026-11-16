using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using VpnHood.AppUi.Common;
using Path = Avalonia.Controls.Shapes.Path;

namespace VpnHood.App.StoreScreenshots;

// The phone or tablet a capture is shown in: a bezel with the app's screen inside it, a status bar
// across the top and a home indicator at the foot.
//
// Drawn rather than photographed, so nothing binary rides in the repo and the geometry stays a few
// numbers per device (DeviceSpec). The bands top and bottom are painted with colours sampled from
// the capture's own edges, so the status bar continues the page instead of guessing a colour that
// drifts whenever the theme changes.
//
// The capture covers the safe strip only, which is exactly the proportion left between the bands -
// so the picture fits without cropping, and a device whose numbers do not add up shows as a
// stretched screen rather than silently cropping the app.
internal static class PhoneFrame
{
    private static readonly Color ScreenBackground = Color.Parse("#0b1440");

    public static Control Build(DeviceSpec device, Bitmap capture, Color topBandColor, Color bottomBandColor)
    {
        // the screen at the mockup's size, and the safe-area bands in the same proportion
        var screenHeight = Math.Round(device.ScreenW * ((double)device.CssHeight / device.CssWidth));
        var bandScale = screenHeight / device.CssHeight;
        var topBand = Math.Round(device.SafeTop * bandScale, 2);
        var bottomBand = Math.Round(device.SafeBottom * bandScale, 2);

        var screen = new Grid {
            RowDefinitions = new RowDefinitions {
                new(topBand, GridUnitType.Pixel),
                new(1, GridUnitType.Star),
                new(bottomBand, GridUnitType.Pixel)
            },
            Children = {
                Band(device, topBandColor, topBand),
                new Image { Source = capture, Stretch = Stretch.UniformToFill },
                BottomBand(device, bottomBandColor, bottomBand)
            }
        };
        Grid.SetRow((Control)screen.Children[1], 1);
        Grid.SetRow((Control)screen.Children[2], 2);

        var phone = new Border {
            Width = device.ScreenW + device.Bezel * 2,
            Height = screenHeight + device.Bezel * 2,
            CornerRadius = new CornerRadius(device.OuterRadius),
            Padding = new Thickness(device.Bezel),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            // brushed metal: the rim catches the light twice, which is what makes it read as a rim
            Background = Gradient(145, device.ScreenW + device.Bezel * 2, screenHeight + device.Bezel * 2, [
                (0.00, "#c9ced8"), (0.18, "#6f747f"), (0.42, "#3c414c"), (0.68, "#767c88"), (1.00, "#b9bfc9")
            ]),
            BoxShadow = BoxShadows.Parse(
                $"0 {device.Bezel * 4} {device.Bezel * 7} #8C000000, " +
                $"0 {device.Bezel} {device.Bezel * 2} #59000000, " +
                "inset 0 0 2 #80FFFFFF"),
            Child = new Border {
                CornerRadius = new CornerRadius(device.ScreenRadius),
                ClipToBounds = true,
                Background = new SolidColorBrush(ScreenBackground),
                Child = screen
            }
        };

        return new Panel {
            Width = device.CssWidth,
            Height = device.CssHeight,
            Background = Backdrop.Brush,
            Children = { phone }
        };
    }

    // The status bar over the sampled colour, with the camera cutout this device wears.
    private static Control Band(DeviceSpec device, Color color, double height)
    {
        var band = new Panel { Background = new SolidColorBrush(color), Children = { StatusBar(device, height) } };

        if (device.Island != null)
            band.Children.Add(new Border {
                Width = device.Island.Width,
                Height = device.Island.Height,
                CornerRadius = new CornerRadius(device.Island.Height / 2),
                Background = Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });

        if (device.PunchHole != null)
            band.Children.Add(new Ellipse {
                Width = device.PunchHole.Size,
                Height = device.PunchHole.Size,
                Fill = Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, height * 0.4 - device.PunchHole.Size / 2, 0, 0)
            });

        return band;
    }

    private static Control BottomBand(DeviceSpec device, Color color, double height)
    {
        return new Panel {
            Background = new SolidColorBrush(color),
            Children = {
                new Rectangle {
                    Width = device.IndicatorWidth,
                    Height = Math.Max(3, height * 0.17),
                    RadiusX = 2,
                    RadiusY = 2,
                    Fill = new SolidColorBrush(Color.Parse("#D9FFFFFF")),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 0, height * 0.3)
                }
            }
        };
    }

    // The time on one side and the signal cluster on the other, in the app's own face so the bar
    // reads the same on every machine that draws it.
    private static Control StatusBar(DeviceSpec device, double height)
    {
        var cluster = new StackPanel {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        if (device.IsAndroidStatus)
            cluster.Children.Add(Wifi(device.StatusFont));
        cluster.Children.Add(SignalBars(device.StatusFont));
        cluster.Children.Add(Battery(device.StatusFont));

        var time = new TextBlock {
            Text = "9:41",
            FontFamily = FontFamily.Parse(AppFonts.TextFamily),
            FontSize = device.StatusFont,
            FontWeight = FontWeight.SemiBold,
            LetterSpacing = 0.2,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        };

        var bar = new Grid {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Margin = new Thickness(device.StatusPad, height * 0.15, device.StatusPad, 0),
            Children = { time, cluster }
        };
        Grid.SetColumn(cluster, 1);
        return bar;
    }

    private static Control SignalBars(double statusFont)
    {
        var full = statusFont * 0.78;
        var bars = new StackPanel {
            Orientation = Orientation.Horizontal,
            Spacing = 1.5,
            Height = full,
            VerticalAlignment = VerticalAlignment.Center
        };
        foreach (var part in new[] { 0.33, 0.55, 0.78, 1.0 })
            bars.Children.Add(new Rectangle {
                Width = statusFont * 0.22,
                Height = full * part,
                RadiusX = 1,
                RadiusY = 1,
                Fill = Brushes.White,
                VerticalAlignment = VerticalAlignment.Bottom
            });
        return bars;
    }

    private static Control Battery(double statusFont)
    {
        return new Border {
            Width = statusFont * 1.74,
            Height = statusFont * 0.87,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.Parse("#A6FFFFFF")),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(1.5),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new Rectangle { RadiusX = 1, RadiusY = 1, Fill = Brushes.White }
        };
    }

    private static Control Wifi(double statusFont)
    {
        var width = statusFont * 0.95;
        var height = statusFont * 0.8;
        return new Path {
            Fill = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Data = new PolylineGeometry([
                new Point(width / 2, height), new Point(0, height * 0.18), new Point(width, height * 0.18)
            ], true)
        };
    }

    // A CSS angle, as a brush: zero points up and the angle turns clockwise, and the line through
    // the middle is long enough that its ends reach the corners - the rule a browser follows. In
    // the box's own pixels rather than in fractions of it, because a fraction of a tall box is a
    // different angle (the rim would catch the light in the wrong place on every device but a
    // square one).
    private static LinearGradientBrush Gradient(double degrees, double width, double height,
        IReadOnlyList<(double Offset, string Color)> stops)
    {
        var radians = degrees * Math.PI / 180;
        var direction = new Point(Math.Sin(radians), -Math.Cos(radians));
        var length = Math.Abs(width * direction.X) + Math.Abs(height * direction.Y);
        var center = new Point(width / 2, height / 2);
        var brush = new LinearGradientBrush {
            StartPoint = new RelativePoint(center - direction * (length / 2), RelativeUnit.Absolute),
            EndPoint = new RelativePoint(center + direction * (length / 2), RelativeUnit.Absolute)
        };
        foreach (var (offset, color) in stops)
            brush.GradientStops.Add(new GradientStop(Color.Parse(color), offset));
        return brush;
    }
}
