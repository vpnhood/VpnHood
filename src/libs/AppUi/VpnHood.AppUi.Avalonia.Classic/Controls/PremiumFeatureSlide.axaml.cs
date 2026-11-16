using Avalonia.Controls;
using VpnHood.AppUi.Avalonia.Classic.Resources;

namespace VpnHood.AppUi.Avalonia.Classic.Controls;

public partial class PremiumFeatureSlide : UserControl
{
    // the rocket slide (no image) is drawn from three pictures; the others from one
    public PremiumFeatureSlide(string? image, string title, string description)
    {
        InitializeComponent();
        RocketBox.IsVisible = image == null;
        SlideImage.IsVisible = image != null;
        if (image != null)
            AppImage.SetSource(SlideImage, AppAssets.ImagePath(image));
        TitleText.Text = title;
        DescriptionText.Text = description;
    }
}
