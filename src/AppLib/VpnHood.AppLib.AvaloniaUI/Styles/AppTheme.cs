using Avalonia.Media;

namespace VpnHood.AppLib.AvaloniaUI.Styles;

// The palette, in code for the places XAML cannot reach (the desktop window's own background):
// the web UI CONNECT theme's background, purple 500 (AppTheme.axaml has the rest).
public static class AppTheme
{
    public static readonly Color Background = Color.Parse("#150E3D");
}
