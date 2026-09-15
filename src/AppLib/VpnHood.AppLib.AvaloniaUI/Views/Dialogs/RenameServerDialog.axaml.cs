using Avalonia.Input;
using Avalonia.Interactivity;
using VpnHood.AppLib.AvaloniaUI.Helpers;

namespace VpnHood.AppLib.AvaloniaUI.Views.Dialogs;

public partial class RenameServerDialog : DialogBase
{
    public RenameServerDialog(string name)
    {
        InitializeComponent();
        NameBox.Text = name;
    }

    public string NewName => NameBox.Text ?? "";

    public override void FocusDefault()
    {
        NameBox.LandFocus();
        NameBox.SelectAll();
    }

    private void OnNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }
}
