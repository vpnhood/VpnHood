namespace VpnHood.Core.Client.Devices.Android.InstalledApps;

// An app as the list shows it, kept until the app's package changes
internal class InstalledAppsCacheItem
{
    public required string AppId { get; init; }

    // the package's last install or update, in Unix milliseconds
    public required long UpdateTime { get; init; }
    public required string AppName { get; init; }
    public required string IconPng { get; init; }
}
