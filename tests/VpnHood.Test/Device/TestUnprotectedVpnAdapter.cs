using System.Net;
using System.Net.Sockets;

namespace VpnHood.Test.Device;

// An adapter that cannot take a socket around its tunnel, with the machine's own DNS servers to ask.
public class TestUnprotectedVpnAdapter : TestNullVpnAdapter
{
    public override bool ProtectSocket(Socket socket) => false;
    public override bool ProtectSocket(Socket socket, IPAddress ipAddress) => false;
}
