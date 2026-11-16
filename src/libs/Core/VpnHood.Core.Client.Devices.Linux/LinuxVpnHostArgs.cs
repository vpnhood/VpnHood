namespace VpnHood.Core.Client.Devices.Linux;

/// <summary>
/// Represents the VPN host's command line, which the daemon writes and the VPN host reads (<c>LinuxVpnHost</c>).
/// </summary>
public sealed class LinuxVpnHostArgs
{
    private const string StorageOption = "--storage";

    /// <summary>Gets the command line's usage: <c>--storage &lt;folder&gt;</c>.</summary>
    public static string Usage => $"{StorageOption} <folder>";

    /// <summary>Gets the app's storage: the kill switch's owner, with the VPN service's folder under it.</summary>
    public required string StorageFolder { get; init; }

    /// <summary>Writes the command line's arguments.</summary>
    /// <returns>The arguments.</returns>
    internal IReadOnlyList<string> ToArgs()
    {
        return [StorageOption, StorageFolder];
    }

    /// <summary>Reads a command line.</summary>
    /// <param name="args">The arguments.</param>
    /// <returns>
    /// The command line, or <see langword="null"/> where the arguments are not <c>--storage</c> and a folder alone.
    /// </returns>
    public static LinuxVpnHostArgs? TryParse(IReadOnlyList<string> args)
    {
        return args is [StorageOption, var storageFolder]
            ? new LinuxVpnHostArgs { StorageFolder = storageFolder }
            : null;
    }
}
