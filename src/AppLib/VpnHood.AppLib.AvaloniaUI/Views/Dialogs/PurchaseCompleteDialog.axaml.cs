using Avalonia.Interactivity;
using VpnHood.AppLib.AvaloniaUI.Helpers;

namespace VpnHood.AppLib.AvaloniaUI.Views.Dialogs;

public partial class PurchaseCompleteDialog : DialogBase
{
    private readonly MainView _host;

    public PurchaseCompleteDialog(MainView host)
    {
        _host = host;
        InitializeComponent();
    }

    // Close is the one way out, and it goes home
    public override bool CanDismiss => false;

    public override void FocusDefault()
    {
        CloseButton.LandFocus();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close(true);
        _host.GoHome();
    }
}
