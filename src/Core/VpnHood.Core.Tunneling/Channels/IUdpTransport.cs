namespace VpnHood.Core.Tunneling.Channels;

public interface IUdpTransport : IDisposable
{
    Task SendAsync(ReadOnlyMemory<byte> buffer);
    Action<Memory<byte>>? DataReceived { get; set; }
    int OverheadLength { get; }
    bool Connected { get; }
}