using System.Globalization;
using System.Net;
using VpnHood.AppUi.Hosting.Desktop.Abstractions;
using VpnHood.AppUi.Hosting.Desktop.Linux.Utils;

namespace VpnHood.AppUi.Hosting.Desktop.Linux;

// Who owns the far end of a loopback connection on Linux: the user its row in /proc/net/tcp names.
// That is root for the service, and this process's own user when the far end is this process (dev).
public sealed class LinuxLoopbackPeerCheck : ILoopbackPeerCheck
{
    public Task<bool> IsOwnedBy(IPEndPoint local, IPEndPoint remote, int processId, CancellationToken cancellationToken)
    {
        var expected = processId == Environment.ProcessId ? LinuxUser.EffectiveUid : 0;

        // the far end's row: its local end is our remote one
        return Task.FromResult(ReadOwner(local: remote, remote: local) == expected);
    }

    private static int? ReadOwner(IPEndPoint local, IPEndPoint remote)
    {
        var localHex = Hex(local);
        var remoteHex = Hex(remote);
        foreach (var line in File.ReadLines("/proc/net/tcp").Skip(1)) {
            // sl local_address rem_address st tx_queue:rx_queue tr:tm->when retrnsmt uid ...
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length > 7 &&
                string.Equals(fields[1], localHex, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(fields[2], remoteHex, StringComparison.OrdinalIgnoreCase))
                return int.Parse(fields[7], CultureInfo.InvariantCulture);
        }

        return null;
    }

    // the address as little-endian hex bytes, the port as big-endian hex, as the kernel writes them
    private static string Hex(IPEndPoint endPoint)
    {
        var address = endPoint.Address.GetAddressBytes();
        var littleEndian = (uint)(address[0] | (address[1] << 8) | (address[2] << 16) | (address[3] << 24));
        return $"{littleEndian:X8}:{endPoint.Port:X4}";
    }
}
