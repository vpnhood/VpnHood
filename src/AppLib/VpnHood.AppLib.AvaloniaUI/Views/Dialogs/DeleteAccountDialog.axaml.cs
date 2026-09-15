using Avalonia.Interactivity;
using VpnHood.AppLib.AvaloniaUI.Helpers;
using VpnHood.AppLib.AvaloniaUI.Resources;

namespace VpnHood.AppLib.AvaloniaUI.Views.Dialogs;

public partial class DeleteAccountDialog : DialogBase
{
    public DeleteAccountDialog()
    {
        InitializeComponent();
        UnderstoodRow.Title = Strings.Current.IUnderstand;
    }

    public override void FocusDefault()
    {
        CancelButton.LandFocus();
    }

    private void OnUnderstoodClick(object? sender, EventArgs e)
    {
        UnderstoodRow.IsChecked = !UnderstoodRow.IsChecked;
        DeleteButton.IsEnabled = UnderstoodRow.IsChecked;
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (UnderstoodRow.IsChecked)
            Close(true);
    }
}
