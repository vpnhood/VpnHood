using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace VpnHood.AppUi.Hosting.Cli.Windows.Internal;

// What sudo is on Linux: a command that needs an administrator, run again as one - one UAC prompt,
// the same command elevated, its exit code back. The elevated copy has no console of its own to
// print to, so what it would have printed is the caller's to say.
internal static class WindowsElevation
{
    private const int ErrorCancelled = 1223;

    // An administrator's full token, or LocalSystem; a filtered token of an administrator is not one.
    public static bool IsElevated {
        get {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public static async Task<int> Run(string executablePath, IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo {
            FileName = executablePath,
            Arguments = string.Join(' ', args.Select(Quote)),
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };

        try {
            using var process = Process.Start(startInfo) ??
                                throw new InvalidOperationException($"Could not run {executablePath} elevated.");
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled) {
            await Console.Error.WriteLineAsync("Cancelled: this needs an administrator's approval.").ConfigureAwait(false);
            return 1;
        }
    }

    private static string Quote(string arg) =>
        arg.Length > 0 && !arg.Any(c => char.IsWhiteSpace(c) || c == '"') ? arg : $"\"{arg.Replace("\"", "\\\"")}\"";
}
