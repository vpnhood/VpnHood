namespace VpnHood.Core.Tunneling.Exceptions;

internal class UdpClientQuotaException(int maxUdpClient)
    : NetFilterException($"Maximum UdpClient has been reached. MaxUdpClient: {maxUdpClient}");