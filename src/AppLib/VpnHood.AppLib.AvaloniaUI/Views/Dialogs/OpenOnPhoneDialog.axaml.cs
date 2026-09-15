using Avalonia.Interactivity;
using VpnHood.AppLib.AvaloniaUI.Helpers;

namespace VpnHood.AppLib.AvaloniaUI.Views.Dialogs;

public partial class OpenOnPhoneDialog : DialogBase
{
    public OpenOnPhoneDialog(Uri url, string title)
    {
        InitializeComponent();
        TitleText.Text = title;
        TitleText.IsVisible = title.Length > 0;
        Qr.Text = url.AbsoluteUri;
        AddressText.Text = url.AbsoluteUri;
    }

    public override void FocusDefault()
    {
        CloseButton.LandFocus();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
