using System.CommandLine;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.AppUi.Hosting.Desktop.Commands;

// The instance, not the tunnel. Thin on purpose - every one of these is a line a person could have
// typed on their platform - but they are the lines nobody remembers, and having them here means
// the instance's name is one this install knows rather than one to look up. "service" is the word
// a person types on every platform, whatever the platform calls the thing underneath.
internal static class ServiceCommand
{
    private const int DefaultLogLines = 100;

    public static Command Create(DesktopPlatform platform)
    {
        var instance = platform.Instance;
        var command = new Command("service", "Start, stop and inspect the background service.") {
            Simple(platform, "start", "Start the VPN service.", async cancellationToken => {
                await instance.Start(mayRegister: true, cancellationToken).Vhc();
                return 0;
            }),
            Simple(platform, "stop", "Stop the VPN service. This disconnects the VPN.", instance.Stop),
            Simple(platform, "restart", "Restart the VPN service.", instance.Restart),
            Simple(platform, "status", "Show what the system says about the service.", instance.ShowStatus),
            CreateLog(platform)
        };

        // Only where the app registers its service itself; elsewhere the package's installer does,
        // and a command that could only refuse is left out of help altogether.
        if (platform.InstanceSetup is { } setup) {
            command.Subcommands.Add(Simple(platform, "install",
                "Register the VPN service, started at boot. The installer runs this.", setup.Install));
            command.Subcommands.Add(Simple(platform, "uninstall",
                "Stop the VPN service and remove it. The uninstaller runs this.", setup.Uninstall));
        }

        return command;
    }

    private static Command Simple(DesktopPlatform platform, string name, string description,
        Func<CancellationToken, Task<int>> action)
    {
        var command = new Command(name, description);
        command.SetAction((_, cancellationToken) => Run(platform, action, cancellationToken));
        return command;
    }

    // A standard user is refused before anything that would ask for elevation; anything thrown is its
    // sentence on stderr.
    private static async Task<int> Run(DesktopPlatform platform, Func<CancellationToken, Task<int>> action,
        CancellationToken cancellationToken)
    {
        try {
            if (!platform.IsAdministrator()) {
                await Console.Error.WriteLineAsync(platform.AdministratorsOnlyMessage).Vhc();
                return 1;
            }

            return await action(cancellationToken).Vhc();
        }
        catch (OperationCanceledException) {
            return 130;
        }
        catch (Exception ex) {
            await Console.Error.WriteLineAsync(ex.Message).Vhc();
            return 1;
        }
    }

    private static Command CreateLog(DesktopPlatform platform)
    {
        var followOption = new Option<bool>("--follow", "-f") {
            Description = "Keep printing as new lines arrive."
        };
        var linesOption = new Option<int>("--lines", "-n") {
            Description = "How many past lines of each of its two parts to print.",
            DefaultValueFactory = _ => DefaultLogLines
        };

        var command = new Command("log", "Show the service log.") { followOption, linesOption };
        command.SetAction((parseResult, cancellationToken) => ServiceLog.Run(platform,
            parseResult.GetValue(followOption), parseResult.GetValue(linesOption), cancellationToken));

        return command;
    }
}
