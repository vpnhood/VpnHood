using VpnHood.AppLib.App;
using VpnHood.AppUi.Hosting.Abstractions;

namespace VpnHood.AppUi.Hosting.Cli;

// What a head says for itself, and only that: the init params every platform takes
// (AppInitParams) and the two answers the commands and the window need. Where the machine keeps
// things and what "the service" is are the platform's answers, in CliPlatform; the daemon's host
// lays its storage out itself, so a desktop head names no storage folder. The options factory runs
// once, in the daemon and nowhere else: the window and the commands hold no VpnHoodApp at all.
public class CliHeadParams : AppInitParams
{
    // The head's UI, which the window runs on the host's main thread against the running daemon:
    // the API over loopback, and the UI's own content extracted under the current user's cache. It
    // comes from the small package this and every UI package reference, so neither references the
    // other.
    public required IDesktopUi Ui { get; init; }

    // The same answer the head gives AppOptions.IsAddAccessKeySupported, and it must be the same
    // one: a head that cannot be given a key has exactly the profile it was built with, so naming
    // a profile, listing them and adding one are all questions with no answer. Those commands and
    // the --profile option are not printed, not parsed and not offered in help when this is false.
    //
    // Required rather than defaulted: it is the one thing that differs between Client and Connect,
    // and a new head that forgets it would silently offer Connect's users a profile list of one.
    public required bool IsAddAccessKeySupported { get; init; }

    // Where this package placed the UI's content, beside the binary. The default is what the
    // Classic assets targets place for every head that imports them.
    public string UiZipAssetPath { get; init; } = "assets/ui.zip";
}
