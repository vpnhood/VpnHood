namespace VpnHood.Core.Client.Devices.WindowsTun;

/// <summary>
/// Represents the VPN host's command line, which the daemon writes and the VPN host reads (<c>WindowsVpnHost</c>).
/// </summary>
public sealed class WindowsVpnHostArgs
{
    private const string StorageOption = "--storage";
    private const string DaemonOption = "--daemon";

    /// <summary>Gets the command line's usage: <c>--storage &lt;folder&gt; [--daemon &lt;name&gt;]</c>.</summary>
    public static string Usage => $"{StorageOption} <folder> [{DaemonOption} <name>]";

    /// <summary>Gets the app's storage: the kill switch's owner, with the VPN service's folder under it.</summary>
    public required string StorageFolder { get; init; }

    /// <summary>
    /// Gets the daemon's Windows service name, under which the VPN host writes the Event Log and whose SID the kill
    /// switch's block lets out; <see langword="null"/> for a dev run's daemon, which runs as the person and is no
    /// service.
    /// </summary>
    public string? DaemonName { get; init; }

    /// <summary>Writes the command line's arguments.</summary>
    /// <returns>The arguments.</returns>
    internal IReadOnlyList<string> ToArgs()
    {
        return DaemonName is null
            ? [StorageOption, StorageFolder]
            : [StorageOption, StorageFolder, DaemonOption, DaemonName];
    }

    /// <summary>Reads a command line.</summary>
    /// <param name="args">The arguments.</param>
    /// <returns>
    /// The command line, or <see langword="null"/> where the arguments are not <c>--storage</c> and a folder, with or
    /// without <c>--daemon</c> and a name after them.
    /// </returns>
    public static WindowsVpnHostArgs? TryParse(IReadOnlyList<string> args)
    {
        return args switch {
            [StorageOption, var storageFolder] => new WindowsVpnHostArgs { StorageFolder = storageFolder },
            [StorageOption, var storageFolder, DaemonOption, var daemonName] =>
                new WindowsVpnHostArgs { StorageFolder = storageFolder, DaemonName = daemonName },
            _ => null
        };
    }
}
