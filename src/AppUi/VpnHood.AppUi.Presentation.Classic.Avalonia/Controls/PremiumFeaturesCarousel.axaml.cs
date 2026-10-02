using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using VpnHood.AppUi.Common;
using VpnHood.AppUi.Hosting.Avalonia;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Controls;

public partial class PremiumFeaturesCarousel : UserControl
{
    private readonly IReadOnlyList<(string? Image, string Title, string Description)> _slides;
    private int _index;

    public PremiumFeaturesCarousel()
    {
        InitializeComponent();
        var s = Strings.Current;
        var intents = VhApp.Intents;
        var features = VhApp.Features;

        // PremiumFeaturesCarousel.vue's carouselItems: a feature the device cannot have is not
        // promised, and a build with no premium tier does not sell speed
        var slides = new List<(string? Image, string Title, string Description, bool IsSupported)> {
            (null, s.UltraFastSpeed, s.UltraFastSpeedDesc, VhApp.IsPremiumSupported),
            ("no-ads.webp", s.RemoveAd, s.RemoveAdDesc, features.IsAdSupported),
            ("more-location.webp", s.MoreLocations, s.MoreLocationsDesc, true),
            ("split-ip.webp", s.SplitIpAddresses, s.SplitIpAddressesPremiumDesc, true),
            ("private-dns.webp", s.PrivateAndCustomDns, s.PrivateAndCustomDnsDesc, intents.IsPrivateDnsSettingsSupported),
            ("quick-launch.webp", s.QuickLaunch, s.QuickLaunchDesc, intents.IsQuickLaunchSupported),
            ("always-on.webp", s.AlwaysOn, s.AlwaysOnPremiumDesc, intents.IsAlwaysOnSettingsSupported),
            ("support.webp", s._247Support, s._247SupportDesc, true)
        };
        _slides = slides.Where(x => x.IsSupported).Select(x => (x.Image, x.Title, x.Description)).ToArray();

        var hasMany = _slides.Count > 1;
        PrevButton.IsVisible = hasMany;
        NextButton.IsVisible = hasMany;
        // a TV's remote never lands on an arrow: its Left and Right slide the carousel instead
        // (SlideToward), so the focus stays with the plans
        PrevButton.Focusable = !VhApp.IsTvUi;
        NextButton.Focusable = !VhApp.IsTvUi;
        for (var i = 0; i < _slides.Count; i++) {
            var index = i;
            var dot = new Border { Classes = { "dot" }, Child = new Ellipse() };
            dot.Tapped += (_, _) => Show(index, reversed: index < _index);
            Dots.Children.Add(dot);
        }
        Dots.IsVisible = hasMany;
        Show(0, reversed: false);
    }

    // A remote's Left or Right: the arrow on that side, which a right-to-left language swaps.
    public void SlideToward(NavigationDirection direction)
    {
        var isNext = (direction == NavigationDirection.Right) != (FlowDirection == FlowDirection.RightToLeft);
        Show(isNext ? _index + 1 : _index - 1, reversed: !isNext);
    }

    // The arrows loop, each sliding its own way across the wrap too, so the last slide's Next brings
    // the first in as the next one; a dot slides by position.
    private void Show(int index, bool reversed)
    {
        if (_slides.Count == 0)
            return;

        var next = (index + _slides.Count) % _slides.Count;
        if (SlideHost.Content != null && next == _index)
            return;

        SlideHost.IsTransitionReversed = reversed;
        _index = next;
        var (image, title, description) = _slides[_index];
        SlideHost.Content = new PremiumFeatureSlide(image, title, description);

        for (var i = 0; i < Dots.Children.Count; i++)
            Dots.Children[i].Classes.Set("active", i == _index);
    }

    private void OnPrevClick(object? sender, RoutedEventArgs e)
    {
        Show(_index - 1, reversed: true);
    }

    private void OnNextClick(object? sender, RoutedEventArgs e)
    {
        Show(_index + 1, reversed: false);
    }
}
