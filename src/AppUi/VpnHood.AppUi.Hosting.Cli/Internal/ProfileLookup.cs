using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Api.ClientProfiles;

namespace VpnHood.AppUi.Hosting.Cli.Internal;

// One way to say which profile, used by every command that takes one. A Guid if that is what was
// typed, otherwise a name - exactly, then as a unique prefix, both ignoring case, because the
// names people give profiles are long and a shell has no picker.
//
// An ambiguous prefix is refused rather than guessed: connecting to the wrong server is not the
// kind of mistake a person notices. The command name is for the hints, which say what to type.
internal static class ProfileLookup
{
    // Which profile a command that names none should act on. Adding a key does not select it -
    // that is the app's behaviour and the window's, where the next tap chooses - so on a device
    // with ONE profile and no choice made, that one is obviously meant. With several and no
    // choice made there is nothing obvious, and guessing is worse than asking.
    public static Guid Resolve(AppInfo info, string? named, string commandName)
    {
        var chosen = Resolve(info.ClientProfileInfos, named, commandName);
        if (chosen != null)
            return chosen.Value;

        if (info.UserSettings.ClientProfileId is { } current &&
            info.ClientProfileInfos.Any(x => x.ClientProfileId == current))
            return current;

        return info.ClientProfileInfos.Count switch {
            1 => info.ClientProfileInfos[0].ClientProfileId,
            0 => throw new InvalidOperationException(
                $"This device has no profile yet. Add one with: {commandName} profile add <access-key>"),
            _ => throw new InvalidOperationException(
                $"No profile is chosen. Name one with --profile, or set the default: " +
                $"{commandName} profile set-default <name>")
        };
    }

    // Null in, null out: no profile named is no answer, which the caller above turns into one.
    public static Guid? Resolve(IReadOnlyList<ClientProfileInfo> profiles, string? named, string commandName)
    {
        if (string.IsNullOrWhiteSpace(named))
            return null;

        if (Guid.TryParse(named, out var clientProfileId))
            return clientProfileId;

        var exact = profiles
            .Where(x => string.Equals(x.ClientProfileName, named, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (exact.Length == 1)
            return exact[0].ClientProfileId;

        var matches = exact.Length > 1
            ? exact
            : profiles
                .Where(x => x.ClientProfileName.StartsWith(named, StringComparison.OrdinalIgnoreCase))
                .ToArray();

        return matches.Length switch {
            1 => matches[0].ClientProfileId,
            0 => throw new InvalidOperationException(
                $"No profile matches '{named}'. See: {commandName} profile list"),
            _ => throw new InvalidOperationException(
                $"'{named}' matches more than one profile: " +
                string.Join(", ", matches.Select(x => x.ClientProfileName)))
        };
    }
}
