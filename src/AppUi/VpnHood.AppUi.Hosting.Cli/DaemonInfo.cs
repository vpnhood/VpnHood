using System.Text.Json;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.AppUi.Hosting.Cli;

// What a running daemon tells the rest of the install about itself. One fact matters: the address
// its local API answers on. It is not a constant, because the local listener takes the configured
// port only when that port is free and whatever the OS hands it when it is not
// (VpnHoodAppWebHost.ResolvePort) - so a window or a command that assumed 4700 would dial a
// stranger, or nothing.
//
// Written when the daemon has bound and deleted when it stops, so its absence is the answer to "is
// it running" that the commands give a person. Where it is written is the platform's
// (IAppCliPaths.DaemonInfoFilePath): the systemd unit's storage on Linux, the service's on Windows.
public class DaemonInfo
{
    public required Uri ApiUrl { get; init; }
    public required int ProcessId { get; init; }
    public required string Version { get; init; }

    public static async Task Write(string filePath, Uri apiUrl, CancellationToken cancellationToken)
    {
        var daemonInfo = new DaemonInfo {
            ApiUrl = apiUrl,
            ProcessId = Environment.ProcessId,
            Version = typeof(DaemonInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"
        };

        Directory.CreateDirectory(Path.GetDirectoryName(filePath) ??
                                  throw new InvalidOperationException($"The daemon file has no folder: {filePath}"));

        await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(daemonInfo), cancellationToken);

        // The readers are the desktop session and a person's shell, neither of them root. On
        // Windows the service's folder passes its own ACL down: everyone reads, only it writes.
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(filePath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
    }

    public static void Delete(string filePath)
    {
        VhUtils.TryInvoke("delete the daemon file", () => File.Delete(filePath));
    }

    // Null when no daemon has written one, or when what it wrote cannot be read: both mean the same
    // thing to a caller - there is nothing here to talk to.
    public static DaemonInfo? Read(string filePath)
    {
        try {
            return File.Exists(filePath)
                ? JsonSerializer.Deserialize<DaemonInfo>(File.ReadAllText(filePath))
                : null;
        }
        catch (Exception) {
            return null;
        }
    }
}
