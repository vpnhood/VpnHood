namespace VpnHood.App.StoreScreenshots;

// A language the whole set is generated in.
//
// Tag is the store's own code for it, which is the folder a set installs into and the suffix the
// working files carry; Culture is the app's language, the one the UI is asked for. They differ
// because a store's spelling is the store's ("pt-BR" on Google Play, "zh-Hans" on the App Store)
// while the app names its languages as its locale files are named.
internal sealed class StoreLocale
{
    public required string Tag { get; init; }
    public required string Culture { get; init; }

    // Where a store spells this locale differently, by store key. A null value means that store
    // has no such language at all, so the set is not installed there - App Store Connect offers no
    // Persian, and a set pushed into a locale a store does not have is a failed upload.
    public Dictionary<string, string?> Stores { get; init; } = [];

    // The folder this locale installs into on a store, or null where the store lacks the language.
    public string? FolderFor(string store)
    {
        return Stores.TryGetValue(store, out var folder) ? folder : Tag;
    }
}
