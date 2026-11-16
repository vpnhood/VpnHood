using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using VpnHood.AppUi.Avalonia.Classic.Controls;
using VpnHood.AppUi.Avalonia.Classic.Helpers;

namespace VpnHood.AppUi.Avalonia.Classic.Views.Dialogs;

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

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        MoreIcon.IsVisible = Scroller.Offset.Y < Scroller.ScrollBarMaximum.Y;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
