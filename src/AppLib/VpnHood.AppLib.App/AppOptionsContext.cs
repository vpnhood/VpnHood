using VpnHood.Net.Toolkit.Assets;

namespace VpnHood.AppLib.App;

// What the platform has resolved by the time it calls the head's options factory, so every
// provider the factory makes is made with the right answers: an account provider reads its saved
// session from the storage path in its constructor, which is why the path cannot be set after.
public class AppOptionsContext
{
    public required string AppId { get; init; }

    // The folder that holds the settings, the profiles and the log.
    public required string StoragePath { get; init; }

    // The files the build's asset packages placed with the app - the IP-location database, the
    // UI's store, the browser page - read the way this platform reads them: from the folder beside
    // the binary on a desktop and in an iOS bundle, from the APK's assets on Android.
    public required IAssetProvider PackagedAssetProvider { get; init; }
}
