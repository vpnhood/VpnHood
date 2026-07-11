namespace VpnHood.Core.Tunneling.Exceptions;

public class UdpClientQuotaException(int maxUdpClient)
    : NetFilterException($"Maximum UdpClient has been reached. MaxUdpClient: {maxUdpClient}");