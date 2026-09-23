using Avalonia.Controls;
using Avalonia.Interactivity;
using VpnHood.AppUi.Common;
using VpnHood.AppUi.Hosting.Avalonia;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Helpers;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Views;

public partial class TurnOffPrivateDnsView : UserControl, IPage
{
    public TurnOffPrivateDnsView(MainView host)
    {
        _ = host;
        InitializeComponent();
        var s = Strings.Current;
        var number = 1;
        foreach (var step in new[] { s.PrivateDnsTurnOffStep1, s.PrivateDnsTurnOffStep2, s.PrivateDnsTurnOffStep3, s.PrivateDnsTurnOffStep4 }) {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            var index = new TextBlock { Text = $"{number++}." };
            index.Classes.Add("step-number");
            var text = new TextBlock();
            text.Classes.Add("step");
            RichText.Apply(text, step);
            Grid.SetColumn(text, 1);
            row.Children.Add(index);
            row.Children.Add(text);
            Steps.Children.Add(row);
        }
        SettingsButton.IsVisible = VhApp.Intents.IsSettingsSupported;
    }

    public void FocusDefault()
    {
        if (SettingsButton.IsVisible) SettingsButton.LandFocus();
        else Header.FocusBack();
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        try {
            await VhApp.Api.Intents.OpenSettings(CancellationToken.None);
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }
}
