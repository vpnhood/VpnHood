namespace VpnHood.AppUi.Presentation.Classic.Avalonia.ViewModels;

// A feature in use, as the home's corner shows it and the badge dialog lists it (FeatureIcons.ts):
// its glyph - two for the split, the fork with the globe - its name and the page it leads to.
public sealed record FeatureBadge(string Icon, string? SecondIcon, string Title, FeaturePage Page)
{
    public bool HasSecondIcon => SecondIcon != null;
}
