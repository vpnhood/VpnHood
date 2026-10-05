using System.Windows;
using System.Windows.Controls;

namespace VpnHood.AppUi.Hosting.WebView.Windows;

// The one message shown in the UI's place (IDesktopUi.RunMessage): the sentence and the button that
// closes it, a fixed width and the height of the words.
internal sealed class WpfMessageWindow : Window
{
    public WpfMessageWindow(string title, string text)
    {
        var closeButton = new Button {
            Content = "Close",
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(16, 4, 16, 4),
            IsDefault = true,
            IsCancel = true
        };
        closeButton.Click += (_, _) => Close();

        Title = title;
        Width = 400;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = new StackPanel {
            Margin = new Thickness(24),
            Children = {
                new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) },
                closeButton
            }
        };
    }
}
