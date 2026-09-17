using Avalonia.Controls;
using Avalonia.Interactivity;
using VpnHood.AppLib.Assets;
using VpnHood.AppUi.Hosting.Avalonia;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Helpers;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Views.Dialogs;

public partial class PremiumCodeCompleteDialog : DialogBase
{
    private readonly MainView _host;

    public PremiumCodeCompleteDialog(MainView host)
    {
        _host = host;
        InitializeComponent();
        var s = Strings.Current;
        var state = AppModel.State;
        var access = state.SessionInfo?.AccessInfo;
        var deviceCount = access?.DevicesSummary?.DeviceCount ?? 0;
        var isShared = deviceCount > 1;

        SharedText.IsVisible = access?.IsNew == false && isShared;
        var valueClass = isShared ? "error" : "active";
        if (access != null) {
            AddRow(s.ActivatedOn, Format.ShortDate(access.CreatedTime), valueClass);
            if (access.ExpirationTime is { } expire)
                AddRow(s.ExpirationDate, Format.ShortDate(expire), valueClass);
            if (isShared)
                AddRow(s.UsedDevice, deviceCount.ToString(), valueClass);
        }
        StatisticsButton.IsVisible = AppModel.IsConnected(state);
    }

    private void AddRow(string label, string value, string valueClass)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var labelText = new TextBlock { Text = $"{label}:" };
        labelText.Classes.Add("body-small");
        labelText.Classes.Add(valueClass);
        var valueText = new TextBlock { Text = value };
        valueText.Classes.Add("body-small");
        valueText.Classes.Add(valueClass);
        Grid.SetColumn(valueText, 1);
        row.Children.Add(labelText);
        row.Children.Add(valueText);
        Info.Children.Add(row);
    }

    public override bool CanDismiss => false;

    public override void FocusDefault()
    {
        CloseButton.LandFocus();
    }

    private void OnStatisticsClick(object? sender, RoutedEventArgs e)
    {
        Close(true);
        _host.GoHome();
        _host.Navigate(new StatisticsView(_host));
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close(true);
        _host.GoHome();
    }
}
