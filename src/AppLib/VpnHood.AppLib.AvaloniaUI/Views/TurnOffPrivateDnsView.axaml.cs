using Avalonia.Controls;
using Avalonia.Interactivity;
using VpnHood.AppLib.AvaloniaUI.Helpers;
using VpnHood.AppLib.AvaloniaUI.Resources;
using VpnHood.Core.Client.Devices.UiContexts;

namespace VpnHood.AppLib.AvaloniaUI.Views;

public partial class TurnOffPrivateDnsView : UserControl, IPage
{
    private readonly VpnHoodApp _app = VpnHoodApp.Instance;

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
        SettingsButton.IsVisible = AppData.Intents.IsSettingsSupported;
    }

    public void FocusDefault()
    {
        if (SettingsButton.IsVisible) SettingsButton.LandFocus();
        else Header.FocusBack();
    }

    private void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        _app.Services.DeviceUiProvider.OpenSettings(AppUiContext.RequiredContext);
    }
}
