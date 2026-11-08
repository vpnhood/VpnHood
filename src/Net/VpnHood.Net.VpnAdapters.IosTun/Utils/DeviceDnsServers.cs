using System.Net;
using System.Runtime.InteropServices;

namespace VpnHood.Net.VpnAdapters.IosTun.Utils;

// The DNS servers the device's resolver asks now, as libresolv reads them: Apple gives an app no other way to read
// them. Under a VPN that serves the device's DNS, they are the VPN's.
internal static class DeviceDnsServers
{
    private const string LibResolv = "/usr/lib/libresolv.9.dylib";
    private const int ResStateSize = 4096; // more than struct __res_state takes on any Apple platform
    private const int SockAddrSize = 128; // union res_9_sockaddr_union
    private const int MaxServers = 3; // MAXNS
    private const byte AfInet = 2;
    private const byte AfInet6 = 30;

    [DllImport(LibResolv, EntryPoint = "res_9_ninit")]
    private static extern int ResNInit(IntPtr state);

    [DllImport(LibResolv, EntryPoint = "res_9_getservers")]
    private static extern int ResGetServers(IntPtr state, IntPtr servers, int count);

    [DllImport(LibResolv, EntryPoint = "res_9_ndestroy")]
    private static extern void ResNDestroy(IntPtr state);

    public static unsafe IReadOnlyList<IPAddress> Read()
    {
        var state = NativeMemory.AllocZeroed(ResStateSize);
        var servers = NativeMemory.AllocZeroed(SockAddrSize * MaxServers);
        try {
            if (ResNInit((IntPtr)state) != 0)
                return [];

            try {
                var count = ResGetServers((IntPtr)state, (IntPtr)servers, MaxServers);
                var addresses = new List<IPAddress>();
                for (var i = 0; i < count; i++) {
                    var sockAddr = new ReadOnlySpan<byte>((byte*)servers + i * SockAddrSize, SockAddrSize);
                    var address = sockAddr[1] switch {
                        AfInet => new IPAddress(sockAddr.Slice(4, 4)),
                        AfInet6 => new IPAddress(sockAddr.Slice(8, 16)),
                        _ => null
                    };
                    if (address != null)
                        addresses.Add(address);
                }

                return addresses;
            }
            finally {
                ResNDestroy((IntPtr)state);
            }
        }
        finally {
            NativeMemory.Free(servers);
            NativeMemory.Free(state);
        }
    }
}
