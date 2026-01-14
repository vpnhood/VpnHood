using System.Net;

namespace VpnHood.Core.Tunneling.Channels;

public interface IUdpTransport : IDisposable
{
    Task SendAsync(Memory<byte> buffer);
    Action<Memory<byte>>? DataReceived { get; set; }
    int OverheadLength { get; }
}