namespace VpnHood.Core.Client.Devices.Android.InstalledApps;

// The installed apps' file: its items hold only under the configuration they were loaded in
internal class InstalledAppsCache
{
    public required string Configuration { get; init; }
    public required IReadOnlyList<InstalledAppsCacheItem> Items { get; init; }
}
