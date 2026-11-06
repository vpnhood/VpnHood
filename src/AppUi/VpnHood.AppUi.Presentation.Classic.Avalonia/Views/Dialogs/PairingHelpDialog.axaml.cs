using Avalonia.Input;
using Avalonia.Interactivity;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Controls;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Helpers;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Views.Dialogs;

public partial class PairingHelpDialog : DialogBase
{
    public PairingHelpDialog()
    {
        InitializeComponent();
    }

    public override void FocusDefault()
    {
        CloseButton.LandFocus();
    }

    private void OnCloseKeyDown(object? sender, KeyEventArgs e)
    {
        if (ArrowKeyScroll.Scroll(Scroller, e.Key))
            e.Handled = true;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
