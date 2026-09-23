using VpnHood.AppLib;
using VpnHood.AppLib.Api;
using VpnHood.Core.Toolkit.Assets;

namespace VpnHood.AppUi.Hosting.Cli;

// What a head says for itself, and only that: the product facts no library can know. Where the
// machine keeps things and what "the service" is are the platform's answers, in CliPlatform.
//
// The UI arrives as a call rather than a type because of which way the references point: the
// Avalonia host already references this layer's app, and a UI package naming it back would turn
// that around. So the head - which references both, as a head does - passes the one line that
// starts its own UI.
public class CliHeadParams
{
    // Built once, by the daemon and by nothing else. The window and the commands hold no
    // VpnHoodApp at all.
    public required Func<AppOptions> AppOptionsFactory { get; init; }

    // Runs the head's UI on the calling thread and returns when the window is gone. The API is the
    // one built over loopback against the running daemon; the assets are the UI's own content,
    // extracted under the current user's cache.
    public required Action<string[], VpnHoodApi, IAssetProvider?> RunUi { get; init; }

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
