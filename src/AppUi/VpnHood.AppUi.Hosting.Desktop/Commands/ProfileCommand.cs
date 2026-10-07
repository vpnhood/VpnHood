using System.CommandLine;
using VpnHood.AppLib.Api;
using VpnHood.AppLib.Api.VpnProfiles;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.AppUi.Hosting.Desktop.Commands;

// The access keys this install holds, and which of them the app is set to. A headless box has no
// screen to paste a key into, so this is the one part of setup that cannot be left to the settings
// file: a key is not a setting, it is added through the app so its token is parsed and its
// locations are read.
internal static class ProfileCommand
{
    public static Command Create(IDesktopPlatform platform)
    {
        return new Command("profile", "Manage access keys and the profiles they make.") {
            CreateList(platform), CreateAdd(platform), CreateRemove(platform), CreateSetDefault(platform)
        };
    }

    private static Command CreateList(IDesktopPlatform platform)
    {
        var jsonOption = new Option<bool>("--json") { Description = "Print the profiles as JSON." };
        var command = new Command("list", "List the profiles on this device.") { jsonOption };

        command.SetAction((parseResult, cancellationToken) => DaemonSession.Run(platform, async (api, token) => {
            var info = await api.App.GetInfo(token).Vhc();
            if (parseResult.GetValue(jsonOption)) {
                await CliPrinter.Json(info.VpnProfileInfos, token).Vhc();
                return 0;
            }

            if (info.VpnProfileInfos.Count == 0) {
                await CliPrinter.Line(
                    $"No profiles. Add one with: {platform.Paths.CommandName} profile add <access-key>", token).Vhc();
                return 0;
            }

            var currentId = info.UserSettings.VpnProfileId;
            foreach (var profile in info.VpnProfileInfos) {
                var marker = profile.VpnProfileId == currentId ? "*" : " ";
                await CliPrinter.Line(
                    $"{marker} {profile.VpnProfileId}  {profile.VpnProfileName}", token).Vhc();
            }

            return 0;
        }, cancellationToken));

        return command;
    }

    private static Command CreateAdd(IDesktopPlatform platform)
    {
        var keyArgument = new Argument<string>("access-key") {
            Description = "An access key (vh://...), or the path of a file holding one."
        };
        var command = new Command("add", "Add a profile from an access key.") { keyArgument };

        command.SetAction((parseResult, cancellationToken) => DaemonSession.Run(platform, async (api, token) => {
            var accessKey = ReadAccessKey(parseResult.GetValue(keyArgument) ??
                                          throw new InvalidOperationException("No access key was given."));
            var profile = await api.VpnProfiles.AddByAccessKey(accessKey, token).Vhc();
            await CliPrinter.Line($"Added: {profile.VpnProfileName} ({profile.VpnProfileId})", token).Vhc();
            return 0;
        }, cancellationToken));

        return command;
    }

    private static Command CreateRemove(IDesktopPlatform platform)
    {
        var profileArgument = new Argument<string>("profile") {
            Description = "The profile to remove, by name or id."
        };
        var command = new Command("remove", "Remove a profile.") { profileArgument };

        command.SetAction((parseResult, cancellationToken) => DaemonSession.Run(platform, async (api, token) => {
            var vpnProfileId = await RequireProfileId(api, parseResult.GetValue(profileArgument),
                platform.Paths.CommandName, token).Vhc();
            await api.VpnProfiles.Delete(vpnProfileId, token).Vhc();
            await CliPrinter.Line("Removed.", token).Vhc();
            return 0;
        }, cancellationToken));

        return command;
    }

    private static Command CreateSetDefault(IDesktopPlatform platform)
    {
        var profileArgument = new Argument<string>("profile") {
            Description = "The profile to use when connect is given none, by name or id."
        };
        var command = new Command("set-default", "Choose the profile the app connects with.") { profileArgument };

        command.SetAction((parseResult, cancellationToken) => DaemonSession.Run(platform, async (api, token) => {
            var info = await api.App.GetInfo(token).Vhc();
            var vpnProfileId = RequireNamed(info.VpnProfileInfos, parseResult.GetValue(profileArgument),
                platform.Paths.CommandName);

            // the whole settings object goes back, as the UI saves it: the API takes the document,
            // not a patch of it
            var userSettings = info.UserSettings;
            userSettings.VpnProfileId = vpnProfileId;
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
        return RequireNamed(info.VpnProfileInfos, profile, commandName);
    }

    private static Guid RequireNamed(IReadOnlyList<VpnProfileInfo> profiles, string? named, string commandName)
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
