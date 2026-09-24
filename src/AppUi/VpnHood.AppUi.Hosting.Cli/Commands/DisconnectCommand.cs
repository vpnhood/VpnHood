using System.CommandLine;
using VpnHood.AppUi.Hosting.Cli.Internal;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.AppUi.Hosting.Cli.Commands;

// Disconnect the tunnel. The instance stays up - it is what the window and the next connect talk
// to - so this is not "service stop", which is the other command and says so.
internal static class DisconnectCommand
{
    public static Command Create(CliPlatform platform)
    {
        var command = new Command("disconnect", "Disconnect the VPN. The service keeps running.");

        command.SetAction((_, cancellationToken) => DaemonSession.Run(platform, async (api, token) => {
            await api.App.Disconnect(token).Vhc();
            await CliPrinter.Line("Disconnected.", token).Vhc();
            return 0;
        }, cancellationToken));

        return command;
    }
}
