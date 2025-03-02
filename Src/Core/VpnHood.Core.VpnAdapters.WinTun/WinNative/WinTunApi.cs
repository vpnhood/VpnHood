using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace VpnHood.Core.VpnAdapters.WinTun.WinNative;

[SuppressMessage("ReSharper", "StringLiteralTypo")]
[SuppressMessage("ReSharper", "IdentifierTypo")]
internal static class WinTunApi
{
    [DllImport("wintun.dll", SetLastError = true)]
    public static extern IntPtr WintunCreateAdapter([MarshalAs(UnmanagedType.LPWStr)] string adapterName, [MarshalAs(UnmanagedType.LPWStr)] string tunnelType, in Guid requestedGuid);
    
    [DllImport("wintun.dll", SetLastError = true)]
    public static extern IntPtr WintunCreateAdapter([MarshalAs(UnmanagedType.LPWStr)] string adapterName, [MarshalAs(UnmanagedType.LPWStr)] string tunnelType, IntPtr requestedGuid);
    
    [DllImport("wintun.dll", SetLastError = true)]
    public static extern IntPtr WintunOpenAdapter([MarshalAs(UnmanagedType.LPWStr)] string adapterName);
    
    [DllImport("wintun.dll")]
    public static extern void WintunCloseAdapter(IntPtr adapter);
    
    [DllImport("wintun.dll", SetLastError = true)]
    public static extern bool WintunDeleteDriver();
    
    [DllImport("wintun.dll")]
    public static extern void WintunGetAdapterLUID(IntPtr adapter, ref Luid luid);
    
    [DllImport("wintun.dll")]
    public static extern int WintunGetRunningDriverVersion();
    
    [DllImport("wintun.dll", SetLastError = true, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public static extern void WintunSetLogger(WintunLoggerCallback newLogger);
    
    [DllImport("wintun.dll", SetLastError = true)]
    public static extern void WintunSetLogger(IntPtr mustBeNull);
    
    [DllImport("wintun.dll", SetLastError = true)]
    public static extern IntPtr WintunStartSession(IntPtr adapter, int capacity);
    
    [DllImport("wintun.dll", SetLastError = true)]
    public static extern IntPtr WintunAllocateSendPacket(IntPtr session, int packetSize);
    
    [DllImport("wintun.dll")]
    public static extern void WintunEndSession(IntPtr session);
    
    [DllImport("wintun.dll")]
    public static extern IntPtr WintunGetReadWaitEvent(IntPtr session);
    
    [DllImport("wintun.dll", SetLastError = true)]
    public static extern IntPtr WintunReceivePacket(IntPtr session, out int size);
    
    [DllImport("wintun.dll")]
    public static extern void WintunReleaseReceivePacket(IntPtr session, IntPtr packet);
    
    [DllImport("wintun.dll", SetLastError = true)]
    public static extern void WintunSendPacket(IntPtr session, IntPtr packet);
}
