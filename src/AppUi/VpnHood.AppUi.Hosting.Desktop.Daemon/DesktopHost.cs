using System.CommandLine;
using System.CommandLine.Help;
using VpnHood.AppUi.Hosting.Desktop.Daemon.Commands;
using VpnHood.AppUi.Hosting.Desktop.Daemon.Platform;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.AppUi.Hosting.Desktop.Daemon;

/// <summary>
/// Provides a desktop head's whole run: one binary with three jobs, chosen by the first word on the command line, the
/// service that holds the app, the window that shows it, and the commands that drive it from a shell.
/// </summary>
/// <remarks>
/// A head gives the product facts only it can give (<see cref="DesktopInitParams"/>), a platform package the machine
/// facts only it can give (<see cref="IDesktopPlatform"/>), and this joins them. The VPN service is a binary of its own
/// beside it, the VPN host, which the service starts at each connect; <c>dev</c> runs the app and the window in one
/// process. Run alone, the window's executable opens the window, and the command a person types prints its help.
/// </remarks>
public static class DesktopHost
{
    // SIGTERM - how the service manager stops the daemon - cancels the running command, and the
    // parser then waits this long for it to finish before it ends the process. Its default of
    // 2 s is too short for the daemon, whose stop disconnects the tunnel first.
    private static readonly TimeSpan ProcessTerminationTimeout = TimeSpan.FromSeconds(10);

    // The daemon's graceful close in a signal's stop, which leaves its quick one the rest.
    private static readonly TimeSpan SignalGracefulCloseTimeout = ProcessTerminationTimeout - TimeSpan.FromSeconds(2);

    /// <summary>
    /// Runs what the command line asks for, and blocks until it ends: the work runs off the calling thread and the UI on
    /// it, so a head calls it from its <c>Main</c>, on Windows its <c>[STAThread]</c> one.
    /// </summary>
    /// <param name="args">The process's command-line arguments.</param>
    /// <param name="initParams">The head's init params.</param>
    /// <param name="platform">What the platform says for itself.</param>
    /// <returns>The process's exit code.</returns>
    public static int Run(string[] args, DesktopInitParams initParams, IDesktopPlatform platform)
    {
        using var mainThread = new MainThreadQueue();
        // ReSharper disable once AccessToDisposedClosure
        var run = Task.Run(() => RunAsync(args, initParams, platform, mainThread, CancellationToken.None));

        // A signal's forced end completes the run without waiting for its command (the parser's
        // ProcessTerminationTimeout), and a UI that did not close when its command was cancelled
        // still holds this thread - which would keep the process alive for good. The process then
        // ends the way the run did.
        run.ContinueWith(completed => {
            // ReSharper disable once AccessToDisposedClosure
            if (mainThread.IsBusy)
                Environment.Exit(completed.IsCompletedSuccessfully ? completed.Result : 1);
        }, TaskScheduler.Default);

        mainThread.RunUntil(run);
        return run.GetAwaiter().GetResult();
    }

    private static async Task<int> RunAsync(string[] args, DesktopInitParams initParams, IDesktopPlatform platform,
        MainThreadQueue mainThread, CancellationToken cancellationToken)
    {
        if (args.Length == 0)
            args = platform.Paths.IsStartedAsCommand ? ["--help"] : ["ui"];

        // Added in the order they are meant to be read, since this is also the order help prints
        // them in: what a person does to the connection, then to their profiles, then to the
        // install itself.
        // A command of its own rather than RootCommand, whose name - the one help's Usage line prints - is
        // the dll's: "VpnHoodClient", where a person typed "vhclient". RootCommand's two options come along.
        var rootCommand = new Command(platform.Paths.CommandName, $"{initParams.AppName} at the command line") {
            Options = { new HelpOption(), new VersionOption() }
        };
        rootCommand.Subcommands.Add(ConnectCommand.Create(platform, initParams.IsAddAccessKeySupported));
        rootCommand.Subcommands.Add(DisconnectCommand.Create(platform));
        rootCommand.Subcommands.Add(StatusCommand.Create(platform));
        rootCommand.Subcommands.Add(LocationsCommand.Create(platform, initParams.IsAddAccessKeySupported));
        rootCommand.Subcommands.Add(AlwaysOnCommand.Create(platform));
        rootCommand.Subcommands.Add(KillSwitchCommand.Create(platform));

        // Only a head that can be given an access key has profiles to manage. On one that cannot,
        // the whole group is absent rather than present and refusing - help that lists a command
        // which always fails is worse than help that never mentions it.
        if (initParams.IsAddAccessKeySupported)
            rootCommand.Subcommands.Add(ProfileCommand.Create(platform));

        // A head that cannot be given a key has its plan from an account or a premium code instead, which a box
        // with no window has no other way to give it.
        if (!initParams.IsAddAccessKeySupported) {
            rootCommand.Subcommands.Add(SignInCommand.Create(platform));
            rootCommand.Subcommands.Add(SignInCommand.CreateSignOut(platform));
            rootCommand.Subcommands.Add(PremiumCodeCommand.Create(platform));
        }

        rootCommand.Subcommands.Add(UpdateCommand.Create(platform));
        rootCommand.Subcommands.Add(ServiceCommand.Create(platform));
        rootCommand.Subcommands.Add(UiCommand.Create(platform, initParams, mainThread));

        // "dev", a debugger's, runs the daemon's app beside the window; help leaves it out.
        rootCommand.Subcommands.Add(DaemonCommand.Create(platform));
        rootCommand.Subcommands.Add(DevCommand.Create(platform, initParams, mainThread));

        var invocationConfiguration = new InvocationConfiguration {
            ProcessTerminationTimeout = ProcessTerminationTimeout
        };

        var parseResult = rootCommand.Parse(args);
        return await parseResult.InvokeAsync(invocationConfiguration, cancellationToken).Vhc();
    }

    /// <summary>
    /// Runs the service where a signal stops it, as on Linux: the parser cancels the token at the signal, and the app's
    /// graceful close then gives way to its quick one after a while, before the process ends.
    /// </summary>
    /// <param name="run">The service's run.</param>
    /// <param name="cancellationToken">The token the parser cancels at the signal.</param>
    /// <returns>A task whose result is the process's exit code.</returns>
    public static async Task<int> HostDaemonStoppedBySignal(DaemonRun run, CancellationToken cancellationToken)
    {
        using var quickClose = new CancellationTokenSource();
        // ReSharper disable once AccessToDisposedClosure
        await using var registration = cancellationToken.Register(() => quickClose.CancelAfter(SignalGracefulCloseTimeout));
        return await run(cancellationToken, quickClose.Token).Vhc();
    }
}
