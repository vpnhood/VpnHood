using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using VpnHood.AppLib.Assets;
using VpnHood.AppUi.Hosting.Avalonia;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Resources;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Controls;

public partial class PremiumFeaturesCarousel : UserControl
{
    private readonly IReadOnlyList<(string? Image, string Title, string Description)> _slides;
    private int _index;

    public PremiumFeaturesCarousel()
    {
        InitializeComponent();
        var s = Strings.Current;
        var intents = AppModel.Intents;
        var features = AppModel.Features;

        // PremiumFeaturesCarousel.vue's carouselItems: a feature the device cannot have is not
        // promised, and a build with no premium tier does not sell speed
        var slides = new List<(string? Image, string Title, string Description, bool IsSupported)> {
            (null, s.UltraFastSpeed, s.UltraFastSpeedDesc, AppModel.IsPremiumSupported),
            ("no-ads.webp", s.RemoveAd, s.RemoveAdDesc, features.IsAdSupported),
            ("more-location.webp", s.MoreLocations, s.MoreLocationsDesc, true),
            ("split-ip.webp", s.SplitIpAddresses, s.SplitIpAddressesPremiumDesc, true),
            ("private-dns.webp", s.PrivateAndCustomDns, s.PrivateAndCustomDnsDesc, intents.IsPrivateDnsSettingsSupported),
            ("quick-launch.webp", s.QuickLaunch, s.QuickLaunchDesc, intents.IsQuickLaunchSupported),
            ("always-on.webp", s.AlwaysOn, s.AlwaysOnPremiumDesc, intents.IsAlwaysOnSettingsSupported),
            ("support.webp", s._247Support, s._247SupportDesc, true)
        };
        _slides = [.. slides.Where(x => x.IsSupported).Select(x => (x.Image, x.Title, x.Description))];

        var hasMany = _slides.Count > 1;
        PrevButton.IsVisible = hasMany;
        NextButton.IsVisible = hasMany;
        for (var i = 0; i < _slides.Count; i++) {
            var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4) };
            Dots.Children.Add(dot);
        }
        Dots.IsVisible = hasMany;
        Show(0);
    }

    public Button FirstButton => NextButton;

    private void Show(int index)
    {
        if (_slides.Count == 0)
            return;
        _index = (index + _slides.Count) % _slides.Count;
        var (image, title, description) = _slides[_index];

        // the rocket slide is drawn from three pictures; the others from one
        RocketBox.IsVisible = image == null;
        SlideImage.IsVisible = image != null;
        if (image != null)
            SlideImage.Source = AppAssets.Image(image);
        TitleText.Text = title;
        DescriptionText.Text = description;

        var highlight = this.TryFindResource("HighlightBrush", out var value) && value is IBrush brush ? brush : Brushes.White;
        for (var i = 0; i < Dots.Children.Count; i++) {
            if (Dots.Children[i] is not Border dot) continue;
            dot.Background = highlight;
            dot.Opacity = i == _index ? 1 : 0.35;
        }
    }

    private void OnPrevClick(object? sender, RoutedEventArgs e)
    {
        Show(_index - 1);
    }

    private void OnNextClick(object? sender, RoutedEventArgs e)
    {
        Show(_index + 1);
    }
}
