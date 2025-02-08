using PacketDotNet;
using VpnHood.Core.Server.Abstractions;

namespace VpnHood.App.Server.Providers.Linux;

internal class LinuxTunProvider : ITunProvider
{
    public LinuxTunProvider Create()
    {
        throw new NotImplementedException();
    }

    public event EventHandler<IPPacket>? OnPacketReceived;

    public Task SendPacket(IPPacket ipPacket)
    {
        throw new NotImplementedException();
    }

    public void Dispose()
    {
        throw new NotImplementedException();
    }
}