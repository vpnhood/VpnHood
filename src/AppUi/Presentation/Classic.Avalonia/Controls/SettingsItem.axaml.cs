using Avalonia.Controls;
using Avalonia.Interactivity;
using VpnHood.AppUi.Hosting.Avalonia;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Helpers;
using VpnHood.AppUi.Common;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Controls;

public partial class SettingsItem : UserControl
{
    private const string LanguagesUrl = "https://github.com/vpnhood/VpnHood.Client.WebUI/tree/main/src/locales";

    public event EventHandler? Clicked;
    public event EventHandler? WarningClicked;

    public SettingsItem()
    {
        InitializeComponent();
    }

    public string Title {
        get => TitleText.Text ?? "";
        set => TitleText.Text = value;
    }

    // A row with nothing to add says nothing rather than leaving a blank line under its title.
    public string Subtitle {
        get => SubtitleText.Text ?? "";
        set {
            SubtitleText.Text = value;
            SubtitleText.IsVisible = !string.IsNullOrEmpty(value);
        }
    }

    // the crown, when the feature is sold and this session has not bought it
    public bool IsPremium {
        get => Crown.IsVisible;
        set => Crown.IsVisible = VhApp.ShowCrown(value);
    }

    // dims the card and blocks the tap; the stored values keep showing
    public bool IsDisabled {
        get => !Card.IsEnabled;
        set => Card.IsEnabled = !value;
    }

    public bool ShowLanguageMore {
        get => LanguageMore.IsVisible;
        set => LanguageMore.IsVisible = value;
    }

    // the state chip: on in the colour named (the healthy green unless the on-state weakens
    // protection), off dimmed
    public void SetStatus(bool isOn, string onText, string offText, StatusColor onColor = StatusColor.EnablePremium)
    {
        StatusChip.IsVisible = true;
        StatusText.Text = isOn ? onText : offText;
        StatusChip.Classes.Set("status-on", isOn && onColor == StatusColor.EnablePremium);
        StatusChip.Classes.Set("switch", isOn && onColor == StatusColor.Switch);
        StatusChip.Classes.Set("warning", isOn && onColor == StatusColor.Warning);
        StatusChip.Classes.Set("status-off", !isOn);
    }

    // a chosen item, in the switch colour
    public void SetSelectedItem(string text)
    {
        StatusChip.IsVisible = true;
        StatusText.Text = text;
        StatusChip.Classes.Set("switch", true);
    }

    public void SetWarning(string? text)
    {
        WarningChip.IsVisible = text != null;
        WarningText.Text = text;
    }

    private void OnCardClick(object? sender, RoutedEventArgs e)
    {
        Clicked?.Invoke(this, EventArgs.Empty);
    }

    private void OnWarningClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        WarningClicked?.Invoke(this, EventArgs.Empty);
    }

    private async void OnLanguageLinkClick(object? sender, RoutedEventArgs e)
    {
        try {
            e.Handled = true;
            var host = this.FindHost();
            if (host != null)
                await host.OpenLink(new Uri(LanguagesUrl), Title);
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }
}
