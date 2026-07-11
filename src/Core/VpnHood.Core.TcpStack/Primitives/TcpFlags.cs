namespace VpnHood.Core.TcpStack.Primitives;

[Flags]
internal enum TcpFlags : byte
{
    None = 0,
    Fin = 0x01,
    Syn = 0x02,
    Rst = 0x04,
    Psh = 0x08,
    Ack = 0x10,
}