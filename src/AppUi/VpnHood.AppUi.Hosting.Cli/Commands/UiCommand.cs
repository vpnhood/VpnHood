using System.CommandLine;
using VpnHood.Net.Toolkit.Assets;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.AppUi.Hosting.Cli.Commands;

// The window, run by whoever is logged in. It holds no VpnHoodApp and needs no privilege: it reads
// and drives the daemon's app over loopback, which is the same API a paired phone's page uses. That
// is what lets it run as a normal user - so xdg-open opens a link in that person's browser, and so
// a Wayland session is not asked to show a root window, which it would refuse.
//
// This is what the desktop entry starts. A person who clicks an icon has nowhere to read an error,
// so a stopped instance is started here rather than reported: on Linux that is systemctl asking
// the session's polkit agent, which is a password box on screen and an answer either way. Waiting
// for it to come up is not done here - DaemonConnection.Open waits for any caller.
internal static class UiCommand
{
    public static Command Create(CliPlatform platform, CliHeadParams head)
    {
        var command = new Command("ui", "Open the app window. This is what the desktop entry starts.");
        command.SetAction((_, cancellationToken) => Run(platform, head, cancellationToken));
        return command;
    }

    private static async Task<int> Run(CliPlatform platform, CliHeadParams head, CancellationToken cancellationToken)
    {
        try {
            if (!await platform.Instance.IsRunning(cancellationToken).Vhc() &&
                await platform.Instance.Start(cancellationToken).Vhc() != 0) {
                await Console.Error.WriteLineAsync(
                    $"Could not start {platform.Paths.InstanceName}.").Vhc();
                return 1;
            }

            using var connection = await DaemonConnection.Open(platform, cancellationToken).Vhc();

            // The UI's own copy of the content, under this user's cache: the daemon extracted its
            // own under root's storage, which a session cannot read, and each provider owns its
            // folder.
            var packagedAssets = new FolderAssetProvider(AppContext.BaseDirectory);
            var uiAssets = new ZipAssetProvider(
                new Asset(packagedAssets, head.UiZipAssetPath), platform.Paths.UiContentCachePath);

            head.RunUi([], connection.Api, uiAssets);
            return 0;
        }
        catch (OperationCanceledException) {
            return 130;
        }
        catch (Exception ex) {
            await Console.Error.WriteLineAsync(ex.Message).Vhc();
            return 1;
        }
    }
}
