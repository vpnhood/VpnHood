using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using VpnHood.AppUi.Common;
using VpnHood.AppUi.Hosting.Avalonia;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Helpers;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Views.Dialogs;

public partial class ViewPremiumCodeDialog : DialogBase
{
    private readonly string _code;

    public ViewPremiumCodeDialog(string code)
    {
        _code = Format.CodeGroups(code);
        InitializeComponent();
        CodeText.Text = _code;
    }

    public override void FocusDefault()
    {
        CopyButton.LandFocus();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        try {
            await Copy(CancellationToken.None);
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async Task Copy(CancellationToken cancellationToken)
    {
        // through the host's shell where it names one (a sandboxed app's, AvaloniaUiHosting.Shell)
        if (AvaloniaUiHosting.Shell is { } shell)
            shell.CopyText(_code);
        else if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(_code);
        else
            return;
        CopyGlyph.Text = Mdi.Check;
        await Task.Delay(2000, cancellationToken);
        CopyGlyph.Text = Mdi.ContentCopy;
    }
}
