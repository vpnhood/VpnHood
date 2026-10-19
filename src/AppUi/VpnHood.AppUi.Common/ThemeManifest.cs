namespace VpnHood.AppUi.Common;

// themes/<theme>/manifest.json, as the UI's build writes it into the store.
internal sealed class ThemeManifest
{
    public int SchemaVersion { get; set; }
    public ManifestColors? Colors { get; set; }

    public sealed class ManifestColors
    {
        public string? WindowBackground { get; set; }
        public string? NavigationBar { get; set; }
        public string? ProgressBar { get; set; }
    }
}
