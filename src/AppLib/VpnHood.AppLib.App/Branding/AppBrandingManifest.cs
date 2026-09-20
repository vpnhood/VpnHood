namespace VpnHood.AppLib.Branding;

// branding/<theme>/manifest.json, as the UI's build writes it into the store.
internal sealed class AppBrandingManifest
{
    public int SchemaVersion { get; set; }
    public BrandingColors? Colors { get; set; }
    public BrandingIcons? Icons { get; set; }

    public sealed class BrandingColors
    {
        public string? WindowBackground { get; set; }
        public string? NavigationBar { get; set; }
        public string? ProgressBar { get; set; }
    }

    public sealed class BrandingIcons
    {
        public string? SystemTrayConnected { get; set; }
        public string? SystemTrayConnecting { get; set; }
        public string? SystemTrayDisconnected { get; set; }
    }
}
