using Avalonia.Interactivity;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Helpers;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Views.Dialogs;

public partial class ConfirmDialog : DialogBase
{
    public ConfirmDialog(string title, string message)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
    }

    public override void FocusDefault()
    {
        NoButton.LandFocus();
    }

    private void OnNoClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnYesClick(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }
}
