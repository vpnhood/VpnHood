using Foundation;

namespace VpnHood.AppLib.App.Ios;

// The app's files on an Apple TV, where every folder an app may write is a cache tvOS empties when the
// device runs low on storage, and only the app's defaults are kept: 500 KB of them, by Apple's guide;
// tvOS warns at 512 KB and ends the app at 1 MB. The budget is half of that, leaving room for the
// defaults' other users - the UI language, the system's own keys - and for the store's encoding.
internal sealed class UserDefaultsStorageProvider(string rootFolderPath)
    : KeyValueStorageProvider(rootFolderPath, budget: 256 * 1024)
{
    private static NSUserDefaults Defaults => NSUserDefaults.StandardUserDefaults;

    protected override string? GetValue(string key) => Defaults.StringForKey(key);
    protected override void SetValue(string key, string value) => Defaults.SetString(value, key);
    protected override void RemoveValue(string key) => Defaults.RemoveObject(key);

    protected override IReadOnlyList<string> GetKeys() =>
        Defaults.ToDictionary().Keys.Select(key => key.ToString() ?? string.Empty).ToArray();
}
