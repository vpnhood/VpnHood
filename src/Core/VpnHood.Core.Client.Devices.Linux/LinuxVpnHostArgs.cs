namespace VpnHood.Core.Client.Devices.Linux;

// The VPN host's command line, which the daemon writes (LinuxVpnHostProcess) and the VPN host reads (LinuxVpnHost).
public sealed class LinuxVpnHostArgs
{
    private const string StorageOption = "--storage";

    public static string Usage => $"{StorageOption} <folder>";

    // The app's storage: the kill switch's owner, with the VPN service's folder under it.
    public required string StorageFolder { get; init; }

    public IReadOnlyList<string> ToArgs()
    {
        return [StorageOption, StorageFolder];
    }

    public static LinuxVpnHostArgs? TryParse(IReadOnlyList<string> args)
    {
        return args is [StorageOption, var storageFolder]
            ? new LinuxVpnHostArgs { StorageFolder = storageFolder }
            : null;
    }
}
