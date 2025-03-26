namespace VpnHood.Core.VpnAdapters.LinuxTun.LinuxNative;
// ReSharper disable CommentTypo

[Flags]
internal enum PollEvent : short
{
    In = 0x001, // POLLIN
    Out = 0x004 // POLLOUT
}