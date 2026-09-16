using Avalonia.Controls;
using Avalonia.Interactivity;
using VpnHood.AppLib.Assets;
using VpnHood.AppLib.AvaloniaUI.Helpers;
using VpnHood.AppLib.AvaloniaUI.Resources;

namespace VpnHood.AppLib.AvaloniaUI.Views;

public partial class FeaturePageView : UserControl, IPage
{
    private readonly MainView _host;
    private readonly Func<Task>? _action;

    public FeaturePageView(MainView host, FeaturePageOptions options)
    {
        _host = host;
        _action = options.Action;
        InitializeComponent();

        BackButton.IsVisible = !AppData.IsTvUi;
        RichText.Apply(TitleText, options.Title);
        DescriptionText.Text = options.Description;
        DescriptionText.IsVisible = options.Description != null;
        FeatureImage.Source = AppAssets.Image(options.Image);
        // shorter on a TV: 240px of art above the controls is a third of a 720 panel
        FeatureImage.MaxHeight = AppData.IsTvUi ? 140 : 240;

        switch (options.Kind) {
            case FeaturePageKind.CloakMode:
                CloakCard.IsVisible = true;
                RichText.Apply(CloakText1, Strings.Current.CloakModeDesc1);
                break;

            case FeaturePageKind.PrivateDnsError when !AppData.IsPremiumUser:
                PrivateDnsCard.IsVisible = true;
                var isCustomized = AppData.IsPrivateDnsCustomized(AppData.State);
                TurnOffButton.IsVisible = isCustomized;
                OrRow.IsVisible = isCustomized;
                break;

            default:
                if (!options.IsPremium || AppData.IsPremiumUser) {
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
        else if (PremiumButton.IsVisible && PremiumCard.IsVisible) PremiumButton.LandFocus();
        else if (TurnOffButton.IsVisible && PrivateDnsCard.IsVisible) TurnOffButton.LandFocus();
        else if (SkipButton.IsVisible) SkipButton.LandFocus();
        else if (BackButton.IsVisible) BackButton.LandFocus();
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
