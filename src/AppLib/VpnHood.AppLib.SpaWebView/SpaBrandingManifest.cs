namespace VpnHood.AppLib.SpaWebView;

internal sealed class SpaBrandingManifest
{
    public int SchemaVersion { get; set; }
    public SpaBrandingColors? Colors { get; set; }
    public SpaBrandingIcons? Icons { get; set; }


    public sealed class SpaBrandingColors
    {
        public string? WindowBackground { get; set; }
        public string? NavigationBar { get; set; }
        public string? ProgressBar { get; set; }
    }

    public sealed class SpaBrandingIcons
    {
        public string? SystemTrayConnected { get; set; }
        public string? SystemTrayConnecting { get; set; }
        public string? SystemTrayDisconnected { get; set; }
    }
}