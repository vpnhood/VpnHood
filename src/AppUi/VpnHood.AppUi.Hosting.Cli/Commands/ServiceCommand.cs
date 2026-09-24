using System.CommandLine;

namespace VpnHood.AppUi.Hosting.Cli.Commands;

// The instance, not the tunnel. Thin on purpose - every one of these is a line a person could have
// typed on their platform - but they are the lines nobody remembers, and having them here means
// the instance's name is one this install knows rather than one to look up. "service" is the word
// a person types on every platform, whatever the platform calls the thing underneath.
internal static class ServiceCommand
{
    private const int DefaultLogLines = 100;

    public static Command Create(CliPlatform platform)
    {
        var instance = platform.Instance;
        return new Command("service", "Start, stop and inspect the background service.") {
            Simple("start", "Start the VPN service.", instance.Start),
            Simple("stop", "Stop the VPN service. This disconnects the VPN.", instance.Stop),
            Simple("restart", "Restart the VPN service.", instance.Restart),
            Simple("status", "Show what the system says about the service.", instance.ShowStatus),
            CreateLog(instance)
        };
    }

    private static Command Simple(string name, string description, Func<CancellationToken, Task<int>> action)
    {
        var command = new Command(name, description);
        command.SetAction((_, cancellationToken) => action(cancellationToken));
        return command;
    }

    private static Command CreateLog(IAppInstanceController instance)
    {
        var followOption = new Option<bool>("--follow", "-f") {
            Description = "Keep printing as new lines arrive."
        };
        var linesOption = new Option<int>("--lines", "-n") {
            Description = "How many past lines to print.",
            DefaultValueFactory = _ => DefaultLogLines
        };

        var command = new Command("log", "Show the service log.") { followOption, linesOption };
        command.SetAction((parseResult, cancellationToken) => instance.ShowLog(
            parseResult.GetValue(followOption), parseResult.GetValue(linesOption), cancellationToken));

        return command;
    }
}
