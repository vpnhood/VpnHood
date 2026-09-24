using System.Text.Json.Nodes;

namespace VpnHood.App.StoreScreenshots;

// One store's set: what that build of the app can do, the devices it is shown on, and the screens.
internal sealed class PlatformSpec
{
    public required string Label { get; init; }

    // Which store this installs to, for the locales that spell their codes differently (StoreLocale).
    public required string Store { get; init; }

    // Where a finished set is copied, inside the repo that owns the store assets, with <locale>
    // standing for that store's folder for the language.
    public required string InstallDir { get; init; }

    // Some stores cap a set (Google Play takes eight per device type). The run still draws every
    // screen worth having; the install ships the leading slice and says what it left out.
    public int? InstallMax { get; init; }

    // What this OS build of the app can do, as a patch over the fixture. Never invented: each
    // value mirrors a device class in this repo, and when a capability changes there it changes here.
    public JsonObject? Patch { get; init; }

    // A patch too long to read inside the configuration, in a file of its own beside it, merged
    // before Patch. What needs one is data derived from the fixture by the app's own rules - the
    // premium-only location list of an iOS build - which is written by deriving it again, never by
    // hand.
    public string? PatchFile { get; init; }

    public required Dictionary<string, DeviceSpec> Devices { get; init; }
    public required IReadOnlyList<ShotSpec> Shots { get; init; }
}
