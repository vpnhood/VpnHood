namespace VpnHood.App.StoreScreenshots;

// What a file is called, in one place.
//
// While it is being made a file carries its language, because every language of a set sits in one
// folder. Installed, it drops it: the store's folder carries the language, and a plain 1.png is
// what fastlane supply - and the catalogues that read a fastlane tree - expect.
internal static class Names
{
    public static string File(DeviceSpec device, ShotSpec shot, StoreLocale locale) =>
        $"{device.Prefix}{shot.Number}_{locale.Tag}.png";

    public static string Fixture(DeviceSpec device, ShotSpec shot, StoreLocale locale) =>
        $"{device.Prefix}{shot.Number}_{locale.Tag}.json";

    public static string Installed(DeviceSpec device, ShotSpec shot) =>
        $"{device.Prefix}{shot.Number}.png";
}
