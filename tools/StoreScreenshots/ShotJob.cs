namespace VpnHood.App.StoreScreenshots;

// One picture to make: which screen, on which device, in which language, for which store.
internal sealed record ShotJob(
    PlatformSpec Platform,
    DeviceSpec Device,
    StoreLocale Locale,
    ShotSpec Shot,
    RunFolders Folders);
