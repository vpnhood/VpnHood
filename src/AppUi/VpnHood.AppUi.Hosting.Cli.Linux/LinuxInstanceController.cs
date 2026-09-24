using System.Diagnostics;

namespace VpnHood.AppUi.Hosting.Cli.Linux;

// The instance on Linux is a systemd unit named after the install, and this drives it the way a
// person would: nothing is parsed and nothing is captured, because systemctl and journalctl already
// print for a human, and a caller here wants exactly what they would have seen had they typed it.
// That also means a polkit agent - the one a desktop session runs - gets to put its password box
// on screen, which is how the window starts a root service without being root.
//
// NOT OsUtils.ExecuteCommandAsync, which is the toolkit's way to run a command and is what the rest
// of the Linux code uses (VpnHoodLinuxApp, LinuxTunVpnAdapter). It redirects both streams and
// returns the output, and that is the one thing these must not do:
//
//   * "sudo" writes "[sudo] password for you:" to STDERR. Redirect it and a person waiting at a
//     prompt sees a command that has simply hung.
//   * "journalctl -f" never ends, so reading it to the end is reading forever; -f is the whole
//     point of "service log -f".
//   * "systemctl status" is a page of formatted text written for a human, not a value to parse.
//
// So the child inherits this process's streams and only its exit code comes back. sudo is prepended
// just when this process is not already root, so the same call works from a session, from a script
// run under sudo, and from a unit.
public class LinuxInstanceController(IAppCliPaths paths) : IAppInstanceController
{
    public string NotRunningHint =>
        $"The {paths.InstanceName} service is not running. Start it with: sudo systemctl start {paths.InstanceName}";

    // The one call whose ANSWER is wanted rather than its output: --quiet prints nothing and says
    // everything in the exit code. No sudo - reading a unit's state needs no privilege.
    public async Task<bool> IsRunning(CancellationToken cancellationToken) =>
        await Run(["systemctl", "is-active", "--quiet", paths.InstanceName], cancellationToken, elevate: false) == 0;

    public Task<int> Start(CancellationToken cancellationToken) =>
        Run(["systemctl", "start", paths.InstanceName], cancellationToken);

    public Task<int> Stop(CancellationToken cancellationToken) =>
        Run(["systemctl", "stop", paths.InstanceName], cancellationToken);

    public Task<int> Restart(CancellationToken cancellationToken) =>
        Run(["systemctl", "restart", paths.InstanceName], cancellationToken);

    // Reading status needs no privilege, so no sudo: being asked for a password to read is the
    // kind of thing that teaches people to type it without looking.
    public Task<int> ShowStatus(CancellationToken cancellationToken) =>
        Run(["systemctl", "status", "--no-pager", paths.InstanceName], cancellationToken, elevate: false);

    public Task<int> ShowLog(bool follow, int lines, CancellationToken cancellationToken)
    {
        string[] arguments = follow
            ? ["journalctl", "-u", paths.InstanceName, "-n", lines.ToString(), "-f"]
            : ["journalctl", "-u", paths.InstanceName, "-n", lines.ToString(), "--no-pager"];

        return Run(arguments, cancellationToken, elevate: false);
    }

    private static async Task<int> Run(IReadOnlyList<string> arguments, CancellationToken cancellationToken,
        bool elevate = true)
    {
        var useSudo = elevate && !LinuxUser.IsRoot;
        var startInfo = new ProcessStartInfo {
            FileName = useSudo ? "sudo" : arguments[0],
            UseShellExecute = false
        };

        foreach (var argument in arguments.Skip(useSudo ? 0 : 1))
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ??
                            throw new InvalidOperationException($"Could not run {startInfo.FileName}.");

        // journalctl -f ends with the caller's Ctrl+C, which reaches the child as its own SIGINT;
        // the wait simply returns when it does.
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }
}
