namespace VpnHood.AppUi.Common;

// branding/<theme>/manifest.json, as the UI's build writes it into the store.
internal sealed class AppBrandingManifest
{
    public int SchemaVersion { get; set; }
    public BrandingColors? Colors { get; set; }

    public sealed class BrandingColors
    {
        public string? WindowBackground { get; set; }
        public string? NavigationBar { get; set; }
        public string? ProgressBar { get; set; }
    }
}
