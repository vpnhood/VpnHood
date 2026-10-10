using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using VpnHood.Net.Toolkit.Graphics;

namespace VpnHood.AppUi.Hosting.Avalonia.Desktop;

// The title bar Avalonia draws in place of an X11 window manager's (AvaloniaDesktopHost), in the
// app's colour. Its buttons are the window's, so the UI's own button styles reach them too - the
// Classic UI's template paints Fluent's close button red at rest - and so they get their template
// back here, in the window's styles, which outrank the application's. Under the pointer a button
// takes a faint wash of its icon's colour, which shows on any bar, rather than Fluent's greys and
// Windows' red. A window that cannot be maximized has no maximize or full-screen button rather than
// greyed ones.
internal static class AvaloniaDrawnTitleBar
{
    public static void Apply(Window window, VhColor? color)
    {
        // the window's own background where the app names no colour, as the message window, if it is
        // one colour
        var barBrush = color is { } barColor
            ? new SolidColorBrush(Color.FromArgb(barColor.A, barColor.R, barColor.G, barColor.B))
            : window.Background as ISolidColorBrush;
        if (barBrush != null)
            window.Resources["TitleBarBackgroundBrush"] = barBrush;

        var buttonStyle = new Style(x => x.OfType<WindowDrawnDecorations>().Template().OfType<Button>());
        buttonStyle.Setters.Add(new Setter(TemplatedControl.TemplateProperty, CreateButtonTemplate()));
        buttonStyle.Setters.Add(new Setter(InputElement.CursorProperty, null));
        window.Styles.Add(buttonStyle);

        if (!window.CanMaximize) {
            window.Styles.Add(HiddenButtonStyle("PART_MaximizeButton"));
            window.Styles.Add(HiddenButtonStyle("PART_FullScreenButton"));
        }

        // in the application's theme: a window takes it only once it opens. A theme with no such brush
        // draws no Fluent buttons.
        if (!window.TryFindResource("CaptionButtonForeground", Application.Current?.ActualThemeVariant, out var value) ||
            value is not ISolidColorBrush foreground)
            return;

        window.Styles.Add(ButtonStateStyle(":pointerover", new SolidColorBrush(foreground.Color, 0.1)));
        window.Styles.Add(ButtonStateStyle(":pressed", new SolidColorBrush(foreground.Color, 0.2)));
    }

    // Fluent's own: the icon on a background only the states colour. The background is set at the
    // template's priority, as XAML sets it; a local value would outrank the states' styles.
    private static FuncControlTemplate<Button> CreateButtonTemplate()
    {
        return new FuncControlTemplate<Button>((button, scope) => {
            var presenter = new ContentPresenter {
                Name = "PART_ContentPresenter",
                [~ContentPresenter.ContentProperty] = button[~ContentControl.ContentProperty]
            };
            presenter.SetValue(ContentPresenter.BackgroundProperty, Brushes.Transparent, BindingPriority.Template);
            return presenter.RegisterInNameScope(scope);
        });
    }

    private static Style HiddenButtonStyle(string name)
    {
        var style = new Style(x => x.OfType<WindowDrawnDecorations>().Template().OfType<Button>().Name(name));
        style.Setters.Add(new Setter(Visual.IsVisibleProperty, false));
        return style;
    }

    private static Style ButtonStateStyle(string pseudoClass, IBrush background)
    {
        var style = new Style(x => x.OfType<WindowDrawnDecorations>().Template().OfType<Button>().Class(pseudoClass)
            .Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"));
        style.Setters.Add(new Setter(ContentPresenter.BackgroundProperty, background));
        return style;
    }
}
