using System.CommandLine;
using VpnHood.AppUi.Hosting.Cli.Commands;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.AppUi.Hosting.Cli;

// One binary, three jobs, chosen by the first word on the command line: the daemon that holds the
// app, the window that shows it, and the commands that drive it from a shell. A head is then the
// product facts only it can give (CliHeadParams), a platform package is the machine facts only it
// can give (CliPlatform), and this joins them.
//
// Typing the name alone opens the window, because that is what a person who typed it wanted and
// what a desktop entry means. Anything a platform's old launchers still pass is that platform's
// to catch before this sees it (LinuxCliHost).
public static class CliHost
{
    public static async Task<int> Run(string[] args, CliHeadParams head, CliPlatform platform,
        CancellationToken cancellationToken)
    {
        if (args.Length == 0)
            args = ["ui"];

        // Added in the order they are meant to be read, since this is also the order help prints
        // them in: what a person does to the connection, then to their profiles, then to the
        // install itself.
        var rootCommand = new RootCommand($"{platform.Paths.InstanceName} - VpnHood at the command line");
        rootCommand.Subcommands.Add(ConnectCommand.Create(platform, head.IsAddAccessKeySupported));
        rootCommand.Subcommands.Add(DisconnectCommand.Create(platform));
        rootCommand.Subcommands.Add(StatusCommand.Create(platform));
        rootCommand.Subcommands.Add(LocationsCommand.Create(platform, head.IsAddAccessKeySupported));

        // Only a head that can be given an access key has profiles to manage. On one that cannot,
        // the whole group is absent rather than present and refusing - help that lists a command
        // which always fails is worse than help that never mentions it.
        if (head.IsAddAccessKeySupported)
            rootCommand.Subcommands.Add(ProfileCommand.Create(platform));

        rootCommand.Subcommands.Add(ServiceCommand.Create(platform));
        rootCommand.Subcommands.Add(UiCommand.Create(platform, head));

        // Only a platform whose instance is this binary run headless has anything for "daemon" to be.
        if (platform.CreateDaemonHost != null)
            rootCommand.Subcommands.Add(DaemonCommand.Create(platform, platform.CreateDaemonHost));

        var parseResult = rootCommand.Parse(args);
        return await parseResult.InvokeAsync(new InvocationConfiguration(), cancellationToken).Vhc();
    }
}
