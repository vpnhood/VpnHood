using Avalonia.Controls;
using Avalonia.Interactivity;
using VpnHood.AppUi.Common;
using VpnHood.AppUi.Hosting.Avalonia;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Helpers;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Resources;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Views;

public partial class FeaturePageView : UserControl, IPage
{
    private readonly MainView _host;
    private readonly Func<Task>? _action;
    private readonly Func<bool, Task>? _switchAction;

    public FeaturePageView(MainView host, FeaturePageOptions options)
    {
        _host = host;
        _action = options.Action;
        _switchAction = options.SwitchAction;
        InitializeComponent();

        Header.SetLargeTitle(TitleText, options.Title);
        DescriptionText.Text = options.Description;
        DescriptionText.IsVisible = options.Description != null;
        AppImage.SetSource(FeatureImage, AppAssets.ImagePath(options.Image));
        // shorter on a TV: 240px of art above the controls is a third of a 720 panel
        FeatureImage.MaxHeight = VhApp.IsTvUi ? 140 : 240;

        switch (options.Kind) {
            case FeaturePageKind.CloakMode:
                CloakCard.IsVisible = true;
                OkButton.IsVisible = options.IsPrompt;
                Header.IsBackVisible = !options.IsPrompt;
                RichText.Apply(CloakText1, Strings.Current.CloakModeDesc1);
                break;

            case FeaturePageKind.Switch when !options.IsPremium || VhApp.IsPremiumUser:
                SwitchItem.IsVisible = true;
                SwitchItem.Title = options.SwitchTitle ?? "";
                SwitchItem.IsOn = options.IsSwitchOn;
                break;

            case FeaturePageKind.Switch:
                PremiumCard.IsVisible = true;
                break;

            case FeaturePageKind.PrivateDnsError when !VhApp.IsPremiumUser:
                PrivateDnsCard.IsVisible = true;
                var isCustomized = VhApp.IsPrivateDnsCustomized(VhApp.State);
                TurnOffButton.IsVisible = isCustomized;
                OrRow.IsVisible = isCustomized;
                break;

            default:
                if (!options.IsPremium || VhApp.IsPremiumUser) {
                    StepsCard.IsVisible = options.Steps.Count > 0 || options.IsActionAvailable || options.ShowSkip;
                    var number = 1;
                    foreach (var step in options.Steps) {
                        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
                        var index = new TextBlock { Text = $"{number++}." };
                        index.Classes.Add("step-number");
                        var text = new TextBlock();
                        text.Classes.Add("step");
                        RichText.Apply(text, step);
                        Grid.SetColumn(text, 1);
                        row.Children.Add(index);
                        row.Children.Add(text);
                        Steps.Children.Add(row);
                    }
                    ActionButton.IsVisible = options.IsActionAvailable;
                    ActionButton.Content = options.ButtonText;
                    SkipButton.IsVisible = options.ShowSkip;
                }
                else {
                    PremiumCard.IsVisible = true;
                }
                break;
        }
    }

    public void FocusDefault()
    {
        if (ActionButton.IsVisible) ActionButton.LandFocus();
        else if (SwitchItem.IsVisible) SwitchItem.FindFirstButton()?.LandFocus();
        else if (OkButton.IsVisible && CloakCard.IsVisible) OkButton.LandFocus();
        else if (PremiumButton.IsVisible && PremiumCard.IsVisible) PremiumButton.LandFocus();
        else if (TurnOffButton.IsVisible && PrivateDnsCard.IsVisible) TurnOffButton.LandFocus();
        else if (SkipButton.IsVisible) SkipButton.LandFocus();
        else Header.FocusBack();
    }

    private async void OnActionClick(object? sender, RoutedEventArgs e)
    {
        try {
            if (_action == null)
                return;
            try {
                await _action();
            }
            catch (Exception ex) {
                await _host.ProcessError(ex);
            }
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    // the setting follows the switch; one that could not be written puts the switch back
    private async void OnSwitchToggled(object? sender, EventArgs e)
    {
        try {
            if (_switchAction == null)
                return;
            try {
                await _switchAction(SwitchItem.IsOn);
            }
            catch (Exception ex) {
                SwitchItem.IsOn = !SwitchItem.IsOn;
                await _host.ProcessError(ex);
            }
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private void OnSkipClick(object? sender, RoutedEventArgs e)
    {
        _host.GoBack();
    }

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        _host.GoBack();
    }

    private void OnPremiumClick(object? sender, RoutedEventArgs e)
    {
        _host.Navigate(new PurchaseSubscriptionView(_host, null));
    }

    private void OnTurnOffClick(object? sender, RoutedEventArgs e)
    {
        _host.Navigate(new TurnOffPrivateDnsView(_host));
    }
}
