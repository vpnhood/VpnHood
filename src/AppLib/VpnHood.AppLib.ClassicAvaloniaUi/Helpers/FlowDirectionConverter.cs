using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace VpnHood.AppLib.ClassicAvaloniaUi.Helpers;

// Strings.IsRightToLeft as the root view's FlowDirection: which way the words run is the content
// package's to know, and it knows no Avalonia; the mirroring of the layout beneath is Avalonia's.
public sealed class FlowDirectionConverter : IValueConverter
{
    public static FlowDirectionConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
