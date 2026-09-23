using System.CommandLine;
using VpnHood.AppLib.Api;
using VpnHood.AppLib.Api.ClientProfiles;
using VpnHood.AppUi.Hosting.Cli.Internal;
using VpnHood.Core.Toolkit.Extensions;

namespace VpnHood.AppUi.Hosting.Cli.Commands;

// The access keys this install holds, and which of them the app is set to. A headless box has no
// screen to paste a key into, so this is the one part of setup that cannot be left to the settings
// file: a key is not a setting, it is added through the app so its token is parsed and its
// locations are read.
internal static class ProfileCommand
{
    public static Command Create(CliPlatform platform)
    {
        return new Command("profile", "Manage access keys and the profiles they make.") {
            CreateList(platform), CreateAdd(platform), CreateRemove(platform), CreateSetDefault(platform)
        };
    }

    private static Command CreateList(CliPlatform platform)
    {
        var jsonOption = new Option<bool>("--json") { Description = "Print the profiles as JSON." };
        var command = new Command("list", "List the profiles on this device.") { jsonOption };

        command.SetAction((parseResult, cancellationToken) => DaemonSession.Run(platform, async (api, token) => {
            var info = await api.App.GetInfo(token).Vhc();
            if (parseResult.GetValue(jsonOption)) {
                await CliPrinter.Json(info.ClientProfileInfos, token).Vhc();
                return 0;
            }

            if (info.ClientProfileInfos.Count == 0) {
                await CliPrinter.Line(
                    $"No profiles. Add one with: {platform.Paths.CommandName} profile add <access-key>", token).Vhc();
                return 0;
            }

            var currentId = info.UserSettings.ClientProfileId;
            foreach (var profile in info.ClientProfileInfos) {
                var marker = profile.ClientProfileId == currentId ? "*" : " ";
                await CliPrinter.Line(
                    $"{marker} {profile.ClientProfileId}  {profile.ClientProfileName}", token).Vhc();
            }

            return 0;
        }, cancellationToken));

        return command;
    }

    private static Command CreateAdd(CliPlatform platform)
    {
        var keyArgument = new Argument<string>("access-key") {
            Description = "An access key (vh://...), or the path of a file holding one."
        };
        var command = new Command("add", "Add a profile from an access key.") { keyArgument };

        command.SetAction((parseResult, cancellationToken) => DaemonSession.Run(platform, async (api, token) => {
            var accessKey = ReadAccessKey(parseResult.GetValue(keyArgument) ??
                                          throw new InvalidOperationException("No access key was given."));
            var profile = await api.ClientProfiles.AddByAccessKey(accessKey, token).Vhc();
            await CliPrinter.Line($"Added: {profile.ClientProfileName} ({profile.ClientProfileId})", token).Vhc();
            return 0;
        }, cancellationToken));

        return command;
    }

    private static Command CreateRemove(CliPlatform platform)
    {
        var profileArgument = new Argument<string>("profile") {
            Description = "The profile to remove, by name or id."
        };
        var command = new Command("remove", "Remove a profile.") { profileArgument };

        command.SetAction((parseResult, cancellationToken) => DaemonSession.Run(platform, async (api, token) => {
            var clientProfileId = await RequireProfileId(api, parseResult.GetValue(profileArgument),
                platform.Paths.CommandName, token).Vhc();
            await api.ClientProfiles.Delete(clientProfileId, token).Vhc();
            await CliPrinter.Line("Removed.", token).Vhc();
            return 0;
        }, cancellationToken));

        return command;
    }

    private static Command CreateSetDefault(CliPlatform platform)
    {
        var profileArgument = new Argument<string>("profile") {
            Description = "The profile to use when connect is given none, by name or id."
        };
        var command = new Command("set-default", "Choose the profile the app connects with.") { profileArgument };

        command.SetAction((parseResult, cancellationToken) => DaemonSession.Run(platform, async (api, token) => {
            var info = await api.App.GetInfo(token).Vhc();
            var clientProfileId = RequireNamed(info.ClientProfileInfos, parseResult.GetValue(profileArgument),
                platform.Paths.CommandName);

            // the whole settings object goes back, as the UI saves it: the API takes the document,
            // not a patch of it
            var userSettings = info.UserSettings;
            userSettings.ClientProfileId = clientProfileId;
            await api.App.SetUserSettings(userSettings, token).Vhc();

            await CliPrinter.Line("Default profile set.", token).Vhc();
            return 0;
        }, cancellationToken));

        return command;
    }

    // remove and set-default name a profile as their one argument, so "none" is a usage error and
    // never the device's current one - deleting whatever happens to be selected is not what an
    // empty argument should mean.
    private static async Task<Guid> RequireProfileId(VpnHoodApi api, string? profile, string commandName,
        CancellationToken cancellationToken)
    {
        var info = await api.App.GetInfo(cancellationToken).Vhc();
        return RequireNamed(info.ClientProfileInfos, profile, commandName);
    }

    private static Guid RequireNamed(IReadOnlyList<ClientProfileInfo> profiles, string? named, string commandName)
    {
        return ProfileLookup.Resolve(profiles, named, commandName) ??
               throw new InvalidOperationException("No profile was given.");
    }

    // A key on the command line is in that person's shell history and in ps; a file is how a
    // script should pass one, so both are taken and the file wins when the argument names one.
    private static string ReadAccessKey(string keyOrPath)
    {
        return File.Exists(keyOrPath) ? File.ReadAllText(keyOrPath).Trim() : keyOrPath.Trim();
    }
}
