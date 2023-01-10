using System;

namespace VpnHood.Tunneling.Exceptions;

public class UdpClientQuotaException : Exception
{
    public UdpClientQuotaException(int maxUdpClient) 
        : base($"Maximum UdpClient has been reached. MaxUdpClient: {maxUdpClient}")
    {

    }
}