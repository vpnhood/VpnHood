using System.CommandLine;
using VpnHood.AppUi.Hosting.Desktop.Commands;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.AppUi.Hosting.Desktop;

// One binary, three jobs, chosen by the first word on the command line: the daemon that holds the
// app, the window that shows it, and the commands that drive it from a shell. A head is then the
// product facts only it can give (DesktopInitParams), a platform package is the machine facts only it
// can give (IDesktopPlatform), and this joins them.
//
// Typing the name alone opens the window, because that is what a person who typed it wanted and
// what a desktop entry means.
public static class DesktopHost
{
    // The whole run, on the calling thread, which is the host's main thread: the UI gets it (on
    // Windows it must be the STA thread Main was given), and it waits while the parser and every
    // command's work run off it (MainThreadQueue).
    public static int Run(string[] args, DesktopInitParams initParams, IDesktopPlatform platform)
    {
        using var mainThread = new MainThreadQueue();
        var run = Task.Run(() => RunAsync(args, initParams, platform, mainThread));

        // A signal's forced end completes the run without waiting for its command (the parser's
        // ProcessTerminationTimeout), and a UI that did not close when its command was cancelled
        // still holds this thread - which would keep the process alive for good. The process then
        // ends the way the run did.
        run.ContinueWith(completed => {
            if (mainThread.IsBusy)
                Environment.Exit(completed.IsCompletedSuccessfully ? completed.Result : 1);
        }, TaskScheduler.Default);

        mainThread.RunUntil(run);
        return run.GetAwaiter().GetResult();
    }

    private static async Task<int> RunAsync(string[] args, DesktopInitParams initParams, IDesktopPlatform platform,
        MainThreadQueue mainThread)
    {
        if (args.Length == 0)
            args = ["ui"];

        // Added in the order they are meant to be read, since this is also the order help prints
        // them in: what a person does to the connection, then to their profiles, then to the
        // install itself.
        var rootCommand = new RootCommand($"{platform.Paths.InstanceName} - VpnHood at the command line");
        rootCommand.Subcommands.Add(ConnectCommand.Create(platform, initParams.IsAddAccessKeySupported));
        rootCommand.Subcommands.Add(DisconnectCommand.Create(platform));
        rootCommand.Subcommands.Add(StatusCommand.Create(platform));
        rootCommand.Subcommands.Add(LocationsCommand.Create(platform, initParams.IsAddAccessKeySupported));

        // Only a head that can be given an access key has profiles to manage. On one that cannot,
        // the whole group is absent rather than present and refusing - help that lists a command
        // which always fails is worse than help that never mentions it.
        if (initParams.IsAddAccessKeySupported)
            rootCommand.Subcommands.Add(ProfileCommand.Create(platform));

        rootCommand.Subcommands.Add(ServiceCommand.Create(platform));
        rootCommand.Subcommands.Add(UiCommand.Create(platform, initParams, mainThread));

        // "dev", a debugger's, runs the daemon's app beside the window; help leaves it out.
        rootCommand.Subcommands.Add(DaemonCommand.Create(platform));
        rootCommand.Subcommands.Add(DevCommand.Create(platform, initParams, mainThread));

        // SIGTERM - how the service manager stops the daemon - cancels the running command, and the
        // parser then waits this long for it to finish before it ends the process. Its default of
        // 2 s is too short for the daemon, whose stop disconnects the tunnel first.
        var invocationConfiguration = new InvocationConfiguration {
            ProcessTerminationTimeout = TimeSpan.FromSeconds(10)
        };

        var parseResult = rootCommand.Parse(args);
        return await parseResult.InvokeAsync(invocationConfiguration, CancellationToken.None).Vhc();
    }
}
