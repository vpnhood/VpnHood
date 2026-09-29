using System.Net;
using System.Runtime.InteropServices;
using VpnHood.AppUi.Hosting.Cli.Channel;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.AppUi.Hosting.Cli.Windows;

// Who owns the far end of a loopback connection on Windows: the process its TCP table row names. The
// row may lag a connect by a moment, so it is read a few times before the answer is no.
public sealed class WindowsLoopbackPeerCheck : ILoopbackPeerCheck
{
    private const int Attempts = 10;
    private static readonly TimeSpan AttemptInterval = TimeSpan.FromMilliseconds(10);
    private const int AfInet = 2;
    private const int TcpTableOwnerPidAll = 5;

    public async Task<bool> IsOwnedBy(IPEndPoint local, IPEndPoint remote, int processId, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++) {
            // the far end's row: its local end is our remote one
            if (ReadOwner(local: remote, remote: local) is { } owner)
                return owner == processId;

            if (attempt >= Attempts)
                return false;

            await Task.Delay(AttemptInterval, cancellationToken).Vhc();
        }
    }

    private static int? ReadOwner(IPEndPoint local, IPEndPoint remote)
    {
        var size = 0;
        _ = GetExtendedTcpTable(IntPtr.Zero, ref size, false, AfInet, TcpTableOwnerPidAll, 0);
        var buffer = Marshal.AllocHGlobal(size);
        try {
            if (GetExtendedTcpTable(buffer, ref size, false, AfInet, TcpTableOwnerPidAll, 0) != 0)
                return null;

            var count = Marshal.ReadInt32(buffer);
            var rows = buffer + sizeof(int);
            var rowSize = Marshal.SizeOf<TcpRowOwnerPid>();
            for (var i = 0; i < count; i++) {
                var row = Marshal.PtrToStructure<TcpRowOwnerPid>(rows + i * rowSize);
                if (PortOf(row.LocalPort) == local.Port && PortOf(row.RemotePort) == remote.Port &&
                    new IPAddress(row.LocalAddr).Equals(local.Address) && new IPAddress(row.RemoteAddr).Equals(remote.Address))
                    return (int)row.OwningPid;
            }

            return null;
        }
        finally {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // the port sits in the low two bytes, in network order
    private static int PortOf(uint port) => (int)(((port & 0xFF) << 8) | ((port >> 8) & 0xFF));

    [StructLayout(LayoutKind.Sequential)]
    private struct TcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
        public uint OwningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int addressFamily,
        int tableClass, uint reserved);
}
