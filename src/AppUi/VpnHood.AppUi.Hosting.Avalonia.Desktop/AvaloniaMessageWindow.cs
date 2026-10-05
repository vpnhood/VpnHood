using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace VpnHood.AppUi.Hosting.Avalonia.Desktop;

// The one message shown in the UI's place (IDesktopUi.RunMessage): the sentence and the button that
// closes it, a fixed width and the height of the words, in the UI's default look.
internal sealed class AvaloniaMessageWindow : Window
{
    public AvaloniaMessageWindow(string title, string text, string closeText, bool isRightToLeft)
    {
        var closeButton = new Button {
            Content = closeText,
            HorizontalAlignment = HorizontalAlignment.Right,
            IsDefault = true,
            IsCancel = true
        };
        closeButton.Click += (_, _) => Close();

        Title = title;
        Width = 400;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        CanMaximize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FlowDirection = isRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        Background = ThemeBackground();
        Content = new StackPanel {
            Margin = new Thickness(24),
            Spacing = 16,
            Children = {
                new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
                closeButton
            }
        };
    }

    // the theme's own background, as the app's window takes it (VpnHoodAvaloniaAppBase)
    private static IBrush? ThemeBackground()
    {
        return Application.Current is { } application && application.TryFindResource("BackgroundBrush", out var value)
            ? value as IBrush
            : null;
    }
}
