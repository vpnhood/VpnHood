using Avalonia.Controls;
using Avalonia.Interactivity;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Helpers;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Controls;

public partial class PromoteRow : UserControl
{
    private readonly Func<Task> _action;

    public PromoteRow(string icon, string title, string description, string buttonText, Func<Task> action)
    {
        _action = action;
        InitializeComponent();
        Icon.Text = icon;
        TitleText.Text = title;
        DescriptionText.Text = description;
        ButtonText.Text = buttonText;
    }

    public void LandFocus()
    {
        Row.LandFocus();
    }

    private async void OnClick(object? sender, RoutedEventArgs e)
    {
        try {
            await _action();
        }
        catch (Exception ex) {
            var host = this.FindHost();
            if (host != null)
                await host.ProcessError(ex);
        }
    }
}
