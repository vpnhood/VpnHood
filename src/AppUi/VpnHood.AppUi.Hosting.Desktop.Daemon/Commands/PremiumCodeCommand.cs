using System.CommandLine;
using VpnHood.AppLib.Api;
using VpnHood.AppLib.Api.VpnProfiles;
using VpnHood.AppUi.Hosting.Desktop.Daemon.Platform;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.AppUi.Hosting.Desktop.Daemon.Commands;

// The premium code, as the window's dialog sets it: the profile write is the one door, which makes the code work
// here at once and hands it to the account when somebody is signed in. For a box with no window, and for a person
// with a code and no account. Bare, it says whether one is set, never the code.
internal static class PremiumCodeCommand
{
    private const string RemoveValue = "remove";

    public static Command Create(IDesktopPlatform platform)
    {
        var valueArgument = new Argument<string?>("code") {
            Description = $"The code to use, or '{RemoveValue}' to take it off. None says whether one is set.",
            Arity = ArgumentArity.ZeroOrOne
        };

        var command = new Command("premium-code", "Set the premium code the app connects with.") { valueArgument };

        command.SetAction((parseResult, cancellationToken) => DaemonSession.Run(platform,
            (api, token) => Run(api, platform.Paths.CommandName, parseResult.GetValue(valueArgument), token),
            cancellationToken));

        return command;
    }

    private static async Task<int> Run(VpnHoodApi api, string commandName, string? value,
        CancellationToken cancellationToken)
    {
        var info = await api.App.GetInfo(cancellationToken).Vhc();
        var vpnProfileId = ProfileLookup.Resolve(info, null, commandName);

        if (value == null) {
            var accessCode = await api.VpnProfiles.GetAccessCode(vpnProfileId, cancellationToken).Vhc();
            await CliPrinter.Line($"Premium code: {(string.IsNullOrEmpty(accessCode) ? "none" : "set")}",
                cancellationToken).Vhc();
            return 0;
        }

        var isRemove = value == RemoveValue;
        try {
            await api.VpnProfiles.Update(vpnProfileId, new VpnProfileUpdateParams {
                AccessCode = new Patch<string?>(isRemove ? null : value.Trim())
            }, cancellationToken).Vhc();
        }
        catch (Exception ex) when (ex is not OperationCanceledException && !isRemove) {
            // one sentence, as the window's dialog has: the app's own is for its log
            throw new InvalidOperationException("That is not a valid premium code.", ex);
        }

        await CliPrinter.Line(isRemove ? "Premium code removed." : $"Premium code saved. Connect with: {commandName} connect",
            cancellationToken).Vhc();
        return 0;
    }
}
