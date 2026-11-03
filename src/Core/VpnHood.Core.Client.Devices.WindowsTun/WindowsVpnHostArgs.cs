namespace VpnHood.Core.Client.Devices.WindowsTun;

// The VPN host's command line, which the daemon writes (WindowsVpnHostProcess) and the VPN host reads (WindowsVpnHost).
public sealed class WindowsVpnHostArgs
{
    private const string StorageOption = "--storage";
    private const string DaemonOption = "--daemon";

    public static string Usage => $"{StorageOption} <folder> {DaemonOption} <name>";

    // The app's storage: the kill switch's owner, with the VPN service's folder under it.
    public required string StorageFolder { get; init; }

    // The daemon's Windows service name, under which the VPN host writes the Event Log and whose SID the kill switch's
    // block lets out.
    public required string DaemonName { get; init; }

    public IReadOnlyList<string> ToArgs()
    {
        return [StorageOption, StorageFolder, DaemonOption, DaemonName];
    }

    public static WindowsVpnHostArgs? TryParse(IReadOnlyList<string> args)
    {
        return args is [StorageOption, var storageFolder, DaemonOption, var daemonName]
            ? new WindowsVpnHostArgs { StorageFolder = storageFolder, DaemonName = daemonName }
            : null;
    }
}
